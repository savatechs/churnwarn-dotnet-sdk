using System.Collections.Concurrent;

namespace ChurnWarn.Sdk.Internal;

/// <summary>Drains the capture queue and posts batches via <see cref="EventBatchTransport"/>.</summary>
internal sealed class BackgroundEventSender : IDisposable
{
    private readonly ChurnWarnOptions _options;
    private readonly EventBatchTransport _transport;
    private readonly ConcurrentQueue<RecordEventInput> _queue = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _pendingCount;
    private int _disposed;

    public BackgroundEventSender(ChurnWarnOptions options)
    {
        _options = options;
        _transport = new EventBatchTransport(options);
        _worker = Task.Run(WorkerLoopAsync);
    }

    public bool TryEnqueue(RecordEventInput input)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        var max = _options.MaxQueueSize;
        if (max > 0 && Volatile.Read(ref _pendingCount) >= max)
        {
            return false;
        }

        _queue.Enqueue(input);
        Interlocked.Increment(ref _pendingCount);
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose (which disposes the semaphore). The item is still queued and
            // will be drained by the final flush in Dispose; nothing must surface to the caller.
        }
        catch (SemaphoreFullException)
        {
            // Extremely unlikely (Int32.MaxValue pending releases); safe to ignore.
        }

        return true;
    }

    public Task UpsertAccountAsync(AccountUpsertInput input, CancellationToken cancellationToken)
        => _transport.UpsertAccountAsync(input, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _wake.Release();

        try
        {
            // Bound the drain so a slow or unresponsive gateway cannot hang the host's shutdown
            // indefinitely (worst case would otherwise be MaxQueueSize/BatchSize requests at the
            // 30s HTTP timeout each). Any events still queued past the deadline are dropped.
            if (!_worker.Wait(_options.ShutdownTimeout))
            {
                SafeReportError(new TimeoutException(
                    $"ChurnWarn shutdown drain exceeded {_options.ShutdownTimeout.TotalSeconds:0.#}s; pending events were dropped."));
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (AggregateException)
        {
            // a background flush faulted during drain — never surface to the host's Shutdown call
        }
        catch (Exception)
        {
            // defensive: Dispose must never throw into the host
        }

        _cts.Dispose();
        _wake.Dispose();
        _transport.Dispose();
    }

    // Invokes the user's error callback without ever letting it throw back into SDK internals.
    private void SafeReportError(Exception ex)
    {
        var cb = _options.OnSendError;
        if (cb is null)
        {
            return;
        }

        try
        {
            cb(ex);
        }
        catch
        {
            // A faulty error callback must never fault the background worker or the host.
        }
    }

    private async Task WorkerLoopAsync()
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, EventBatchTransport.GatewayMaxBatchSize);
        var flushInterval = _options.FlushInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(5)
            : _options.FlushInterval;
        var buffer = new List<RecordEventInput>(batchSize);

        while (true)
        {
            var shuttingDown = Volatile.Read(ref _disposed) != 0;
            if (shuttingDown && Volatile.Read(ref _pendingCount) == 0)
            {
                break;
            }

            try
            {
                if (!shuttingDown)
                {
                    await _wake.WaitAsync(flushInterval, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // continue to drain
            }
            catch (ObjectDisposedException)
            {
                // semaphore disposed mid-shutdown — fall through to a final drain and exit
            }

            try
            {
                await DrainQueueAsync(buffer, batchSize, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The worker must survive any unexpected fault (a throwing callback, a serialization
                // edge case, etc.). Report it and keep looping so capture never becomes a black hole.
                SafeReportError(ex);
            }
        }
    }

    private RecordEventInput ApplyPrivacy(RecordEventInput item)
    {
        var result = item;

        // User hook runs first (transform/redact), then built-in redaction still applies — so a
        // custom hook cannot silently disable payload redaction. Matches the Node/Python SDKs.
        if (_options.OnBeforeEnqueue is { } hook)
        {
            try
            {
                result = hook(result) ?? item;
            }
            catch
            {
                result = item;
            }
        }

        if (_options.RedactPayload)
        {
            try
            {
                result = TelemetryPrivacy.RedactRecord(result);
            }
            catch
            {
                // RedactRecord already swallows internally; belt-and-suspenders so ApplyPrivacy never throws.
            }
        }

        return result;
    }

    private async Task DrainQueueAsync(List<RecordEventInput> buffer, int batchSize, CancellationToken cancellationToken)
    {
        while (true)
        {
            buffer.Clear();
            while (buffer.Count < batchSize && _queue.TryDequeue(out var item))
            {
                buffer.Add(ApplyPrivacy(item));
                Interlocked.Decrement(ref _pendingCount);
            }

            if (buffer.Count == 0)
            {
                return;
            }

            var snapshot = buffer.ToArray();
            try
            {
                await _transport.SendBatchAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SafeReportError(ex);
            }

            if (buffer.Count < batchSize)
            {
                return;
            }
        }
    }
}
