using ChurnWarn.Sdk.Internal;

namespace ChurnWarn.Sdk;

/// <summary>
/// ChurnWarn event capture SDK. Call <see cref="Initialize"/> once at startup, then
/// <see cref="CaptureEvent(RecordEventInput)"/> from anywhere; events are queued and sent in
/// the background.
/// </summary>
public static class ChurnWarn
{
    private static readonly object Gate = new();
    private static BackgroundEventSender? _sender;
    private static ChurnWarnOptions? _options;

    /// <summary>Starts the background batch sender. Safe to call once per process.</summary>
    public static void Initialize(ChurnWarnOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BaseUrl is null)
        {
            throw new ArgumentException("BaseUrl is required.", nameof(options));
        }

        lock (Gate)
        {
            _sender?.Dispose();
            _options = options;
            _sender = new BackgroundEventSender(options);
        }
    }

    /// <summary>
    /// Enqueues an event for background delivery. Returns immediately; never throws for network errors.
    /// </summary>
    /// <exception cref="InvalidOperationException">When <see cref="Initialize"/> was not called.</exception>
    public static void CaptureEvent(
        string externalAccountId,
        string eventType,
        object? payload = null,
        DateTimeOffset? occurredAt = null,
        string? idempotencyKey = null,
        Guid? tenantId = null,
        string? source = null)
    {
        CaptureEvent(new RecordEventInput(
            externalAccountId,
            eventType,
            source,
            occurredAt,
            PayloadJson: null,
            payload,
            idempotencyKey,
            tenantId));
    }

    /// <summary>
    /// Enqueues an event for background delivery. Returns immediately; never throws for network errors.
    /// </summary>
    /// <exception cref="InvalidOperationException">When <see cref="Initialize"/> was not called.</exception>
    public static void CaptureEvent(RecordEventInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ExternalAccountId))
        {
            throw new ArgumentException("ExternalAccountId is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.EventType))
        {
            throw new ArgumentException("EventType is required.", nameof(input));
        }

        var sender = Volatile.Read(ref _sender)
            ?? throw new InvalidOperationException("Call ChurnWarn.Initialize before CaptureEvent.");

        // Enqueue raw — redaction happens in the background worker thread (BackgroundEventSender.ApplyPrivacy)
        if (!sender.TryEnqueue(input))
        {
            var max = _options?.MaxQueueSize ?? 0;
            if (max > 0)
            {
                // The user callback must never throw into the host's capture path.
                var cb = _options?.OnSendError;
                if (cb is not null)
                {
                    try
                    {
                        cb(new InvalidOperationException(
                            $"ChurnWarn capture queue is full (max {max}). Event was dropped."));
                    }
                    catch
                    {
                        // swallow — a faulty error callback must not surface to CaptureEvent callers
                    }
                }
            }
        }
    }

    /// <summary>
    /// Upserts slow-changing account facts via PUT /api/accounts/{externalId}. Only non-null
    /// fields are written. Unlike <see cref="CaptureEvent(RecordEventInput)"/>, this awaits the
    /// request and throws <see cref="ChurnWarnApiException"/> on a non-2xx response.
    /// </summary>
    /// <exception cref="InvalidOperationException">When <see cref="Initialize"/> was not called.</exception>
    public static Task UpsertAccountAsync(AccountUpsertInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ExternalAccountId))
        {
            throw new ArgumentException("ExternalAccountId is required.", nameof(input));
        }

        var sender = Volatile.Read(ref _sender)
            ?? throw new InvalidOperationException("Call ChurnWarn.Initialize before UpsertAccountAsync.");

        return sender.UpsertAccountAsync(input, cancellationToken);
    }

    /// <summary>Flushes pending events and stops the background sender.</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _sender?.Dispose();
            _sender = null;
            _options = null;
        }
    }
}
