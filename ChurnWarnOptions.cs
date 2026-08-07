namespace ChurnWarn.Sdk;

/// <summary>Configuration for <see cref="ChurnWarn.Initialize"/>.</summary>
public sealed class ChurnWarnOptions
{
    /// <summary>Gateway root URL without trailing slash (e.g. https://api.example.com).</summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>Bearer JWT (with or without "Bearer " prefix). Ignored when <see cref="ApiKey"/> is set.</summary>
    public string? ApiToken { get; init; }

    /// <summary>Ingestion key sent as <c>X-Api-Key</c>. Preferred for server-side capture.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Default tenant id on captured events when not set per event.</summary>
    public Guid? DefaultTenantId { get; init; }

    /// <summary>Default <c>source</c> field on events (max 50).</summary>
    public string DefaultSource { get; init; } = "dotnet_sdk";

    /// <summary>Max events per background flush HTTP request (Gateway limit is 500).</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>How often the background sender drains the queue when events are pending.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Max time <see cref="ChurnWarn.Shutdown"/> waits for the final drain before dropping pending events.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Max queued events before <see cref="ChurnWarn.CaptureEvent(RecordEventInput)"/> drops new events (0 = unbounded).</summary>
    public int MaxQueueSize { get; init; } = 10_000;

    /// <summary>
    /// Retry attempts for a failed batch flush after the first try (0 disables retry). Only transient
    /// failures are retried: network/timeout errors and HTTP 429/5xx. Batches are idempotent
    /// (per-event <c>idempotencyKey</c>), so retries never duplicate events server-side.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Base delay for exponential backoff between retries (with jitter).</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound on any single backoff delay between retries.</summary>
    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional callback when a background flush fails (never thrown to <see cref="ChurnWarn.CaptureEvent(RecordEventInput)"/> callers).</summary>
    public Action<Exception>? OnSendError { get; init; }

    /// <summary>When true (default), payloads are redacted for common sensitive patterns before enqueue.</summary>
    public bool RedactPayload { get; init; } = true;

    /// <summary>
    /// Optional hook to transform events before delivery. Runs in the background worker; built-in
    /// redaction (when <see cref="RedactPayload"/> is true) still applies afterward, so the hook
    /// cannot disable redaction. A throwing hook is ignored (the original event is used).
    /// </summary>
    public Func<RecordEventInput, RecordEventInput>? OnBeforeEnqueue { get; init; }
}
