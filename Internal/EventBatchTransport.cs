using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurnWarn.Sdk.Internal;

/// <summary>HTTP batch transport used by the background sender.</summary>
internal sealed class EventBatchTransport : IDisposable
{
    internal const int GatewayMaxBatchSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ChurnWarnOptions _options;
    private readonly bool _disposeHttp;

    public EventBatchTransport(ChurnWarnOptions options)
    {
        _options = options;
        _disposeHttp = true;
        var baseUri = options.BaseUrl.ToString().TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUri + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        ApplyAuthHeaders(_http, options);
    }

    public void Dispose()
    {
        if (_disposeHttp)
        {
            _http.Dispose();
        }
    }

    public async Task SendBatchAsync(IReadOnlyList<RecordEventInput> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var batchTenant = ResolveBatchTenant(events, _options);

        for (var i = 0; i < events.Count; i += GatewayMaxBatchSize)
        {
            var take = Math.Min(GatewayMaxBatchSize, events.Count - i);
            var chunk = new RecordEventInput[take];
            for (var j = 0; j < take; j++)
            {
                chunk[j] = events[i + j];
            }

            var apiEvents = chunk.Select(c => ToApiBody(c, _options, omitTenant: true)).ToList();
            var batchBody = new BatchRequestBody(batchTenant, apiEvents);
            await PostBatchChunkWithRetryAsync(batchBody, cancellationToken).ConfigureAwait(false);
        }
    }

    // POSTs one chunk, retrying transient failures (network/timeout, HTTP 429/5xx) with
    // exponential backoff + jitter. Batches are idempotent (per-event idempotencyKey), so a
    // retry after an ambiguous failure never duplicates events server-side.
    private async Task PostBatchChunkWithRetryAsync(BatchRequestBody batchBody, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.MaxRetries);

        for (var attempt = 0; ; attempt++)
        {
            var retryable = false;
            try
            {
                using var response = await _http
                    .PostAsJsonAsync("api/events/batch", batchBody, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                var status = (int)response.StatusCode;
                if (!(attempt < maxRetries && IsRetryableStatus(status)))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new ChurnWarnApiException(
                        status, body, $"ChurnWarn API error {status}: {response.ReasonPhrase}.");
                }

                retryable = true;
            }
            catch (ChurnWarnApiException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransientException(ex))
            {
                // transient network/timeout error — fall through to back off and retry
                retryable = true;
            }

            if (retryable)
            {
                await Task.Delay(ComputeBackoff(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryableStatus(int status) => status == 429 || (status >= 500 && status <= 599);

    private static bool IsTransientException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException or System.IO.IOException;

    private TimeSpan ComputeBackoff(int attempt)
    {
        var baseMs = _options.RetryBaseDelay.TotalMilliseconds;
        if (baseMs <= 0) baseMs = 500;
        var maxMs = _options.RetryMaxDelay.TotalMilliseconds;
        if (maxMs <= 0) maxMs = 30_000;

        var exp = baseMs * Math.Pow(2, attempt - 1);
        var capped = Math.Min(maxMs, exp);
        // Equal jitter: half fixed, half random — spreads retries without collapsing to zero wait.
        var jittered = capped / 2 + Random.Shared.NextDouble() * (capped / 2);
        return TimeSpan.FromMilliseconds(jittered);
    }

    public async Task UpsertAccountAsync(AccountUpsertInput input, CancellationToken cancellationToken)
    {
        var ext = input.ExternalAccountId.Trim();
        if (ext.Length == 0)
        {
            throw new ArgumentException("ExternalAccountId is required.", nameof(input));
        }

        if (ext.Length > 100)
        {
            ext = ext[..100];
        }

        var body = new AccountApiBody(
            input.Name,
            input.Email,
            Lower(input.Kind),
            input.BusinessType,
            input.MonetaryValue,
            Lower(input.ValueBasis),
            input.Currency,
            input.PlanKey,
            input.LifecycleStage,
            input.RenewalAt,
            Lower(input.Status),
            Lower(input.Role),
            input.Attributes);

        using var response = await _http
            .PutAsJsonAsync($"api/accounts/{Uri.EscapeDataString(ext)}", body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var respBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ChurnWarnApiException(
                (int)response.StatusCode,
                respBody,
                $"ChurnWarn API error {(int)response.StatusCode}: {response.ReasonPhrase}.");
        }
    }

    private static string? Lower(string? value) => value?.Trim().ToLowerInvariant();

    private static void ApplyAuthHeaders(HttpClient http, ChurnWarnOptions options)
    {
        var apiKey = options.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
            return;
        }

        var token = options.ApiToken?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException(
                "ChurnWarnOptions requires either ApiKey (X-Api-Key) or ApiToken (Bearer JWT).");
        }

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(token));
    }

    private static string NormalizeBearerToken(string token)
    {
        const string prefix = "Bearer ";
        return token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? token[prefix.Length..].Trim()
            : token;
    }

    private static Guid? ResolveBatchTenant(IReadOnlyList<RecordEventInput> events, ChurnWarnOptions options)
    {
        Guid? resolved = null;
        foreach (var e in events)
        {
            if (!e.TenantId.HasValue)
            {
                continue;
            }

            var g = e.TenantId!.Value;
            if (resolved is null)
            {
                resolved = g;
            }
            else if (resolved.Value != g)
            {
                throw new ArgumentException(
                    "All RecordEventInput.TenantId values in a batch must be null or the same Guid.");
            }
        }

        return resolved ?? options.DefaultTenantId;
    }

    private static SingleEventApiBody ToApiBody(RecordEventInput input, ChurnWarnOptions options, bool omitTenant)
    {
        var tenant = omitTenant ? null : (input.TenantId ?? options.DefaultTenantId);
        var source = string.IsNullOrWhiteSpace(input.Source) ? options.DefaultSource : input.Source!;
        if (source.Length > 50)
        {
            source = source[..50];
        }

        var occurred = input.OccurredAt ?? DateTimeOffset.UtcNow;
        var payload = ResolvePayloadJson(input);
        var idem = string.IsNullOrWhiteSpace(input.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : input.IdempotencyKey.Trim();
        if (idem.Length > 200)
        {
            idem = idem[..200];
        }

        var ext = input.ExternalAccountId.Trim();
        if (ext.Length > 100)
        {
            ext = ext[..100];
        }

        var evt = input.EventType.Trim();
        if (evt.Length > 100)
        {
            evt = evt[..100];
        }

        return new SingleEventApiBody(
            omitTenant ? null : tenant,
            ext,
            evt,
            source,
            occurred,
            payload,
            idem);
    }

    private static string ResolvePayloadJson(RecordEventInput input)
    {
        if (!string.IsNullOrEmpty(input.PayloadJson))
        {
            return input.PayloadJson;
        }

        if (input.Payload is null)
        {
            return "{}";
        }

        try
        {
            return JsonSerializer.Serialize(input.Payload, JsonOptions);
        }
        catch
        {
            return "{}";
        }
    }

    private sealed record SingleEventApiBody(
        [property: JsonPropertyName("tenantId")] Guid? TenantId,
        [property: JsonPropertyName("externalAccountId")] string ExternalAccountId,
        [property: JsonPropertyName("eventType")] string EventType,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("payload")] string Payload,
        [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey);

    private sealed record BatchRequestBody(
        [property: JsonPropertyName("tenantId")] Guid? TenantId,
        [property: JsonPropertyName("events")] IReadOnlyList<SingleEventApiBody> Events);

    // Null properties are omitted (JsonIgnoreCondition.WhenWritingNull) so only provided fields are sent.
    private sealed record AccountApiBody(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("businessType")] string? BusinessType,
        [property: JsonPropertyName("monetaryValue")] decimal? MonetaryValue,
        [property: JsonPropertyName("valueBasis")] string? ValueBasis,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("planKey")] string? PlanKey,
        [property: JsonPropertyName("lifecycleStage")] string? LifecycleStage,
        [property: JsonPropertyName("renewalAt")] DateTimeOffset? RenewalAt,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("attributes")] IReadOnlyDictionary<string, object?>? Attributes);
}
