namespace ChurnWarn.Sdk;

/// <summary>Logical event to send to the Gateway.</summary>
/// <param name="ExternalAccountId">Account id in your system (required, max 100 chars).</param>
/// <param name="EventType">Event type string — use <see cref="Metrics"/>, <see cref="RawEvents"/>, or a custom mapped name (max 100 chars).</param>
/// <param name="Source">Overrides <see cref="ChurnWarnOptions.DefaultSource"/> for this event.</param>
/// <param name="OccurredAt">When the event happened. Defaults to send time when null.</param>
/// <param name="PayloadJson">If set, used as the API payload string (already JSON). Otherwise <paramref name="Payload"/> is serialized.</param>
/// <param name="Payload">Serialized to JSON when <paramref name="PayloadJson"/> is null.</param>
/// <param name="IdempotencyKey">De-duplication key. A new key is generated per event at send time when null.</param>
/// <param name="TenantId">Overrides <see cref="ChurnWarnOptions.DefaultTenantId"/> for this event.</param>
public sealed record RecordEventInput(
    string ExternalAccountId,
    string EventType,
    string? Source = null,
    DateTimeOffset? OccurredAt = null,
    string? PayloadJson = null,
    object? Payload = null,
    string? IdempotencyKey = null,
    Guid? TenantId = null);
