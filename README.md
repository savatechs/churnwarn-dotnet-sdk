# ChurnWarn .NET SDK

Fire-and-forget event capture for the ChurnWarn Gateway. Call **`ChurnWarn.Initialize`** once, then **`ChurnWarn.CaptureEvent`** from your app; events are queued and sent in the background in configurable batches.

## Requirements

- .NET 8.0+
- Gateway **API key** (`X-Api-Key`) or **Bearer** JWT

## Install

```bash
dotnet add path/to/your/App.csproj reference path/to/churn_warn_dotnet_sdk/ChurnWarn.Sdk.csproj
```

## Usage

```csharp
using ChurnWarn.Sdk;

// Once at startup (e.g. Program.cs)
ChurnWarn.Initialize(new ChurnWarnOptions
{
    BaseUrl = new Uri("https://your-gateway.example.com"),
    ApiKey = Environment.GetEnvironmentVariable("CHURNWARN_API_KEY"),
    DefaultTenantId = Guid.Parse("..."),
    DefaultSource = "my_app",
    BatchSize = 50,
    FlushInterval = TimeSpan.FromSeconds(5),
    OnSendError = ex => Console.Error.WriteLine(ex)
});

// Anywhere in your app — sync, non-blocking
ChurnWarn.CaptureEvent("user-42", RawEvents.AppLogin);
ChurnWarn.CaptureEvent("user-42", Metrics.FeatureUsed, payload: new { feature = "reports" });

// Optional: flush and stop on shutdown
ChurnWarn.Shutdown();
```

## API

| Method | Description |
|--------|-------------|
| `Initialize(ChurnWarnOptions)` | Starts the background batch sender |
| `CaptureEvent(...)` | Enqueues one event (returns immediately) |
| `UpsertAccountAsync(AccountUpsertInput)` | Writes account facts to `PUT /api/accounts` (awaited; throws on non-2xx) |
| `Shutdown()` | Drains the queue and stops the sender |

`CaptureEvent` does not throw for network failures. Configure **`OnSendError`** to observe background send errors. If the queue exceeds **`MaxQueueSize`**, new events are dropped and **`OnSendError`** is invoked.

## Options

| Property | Default | Description |
|----------|---------|-------------|
| `BaseUrl` | required | Gateway root URL |
| `ApiKey` | — | `X-Api-Key` header (preferred for servers) |
| `ApiToken` | — | Bearer JWT when `ApiKey` is not set |
| `DefaultTenantId` | — | Applied when events omit `TenantId` |
| `DefaultSource` | `dotnet_sdk` | Event `source` field |
| `BatchSize` | `50` | Max events per flush (≤ 500) |
| `FlushInterval` | `5s` | Max wait before sending a non-empty queue |
| `MaxQueueSize` | `10000` | `0` = unbounded |
| `ShutdownTimeout` | `15s` | Max time `Shutdown()` waits for the final drain before dropping pending events |
| `MaxRetries` | `3` | Retry attempts after the first try for a failed flush (`0` disables) |
| `RetryBaseDelay` | `500ms` | Base delay for exponential backoff between retries |
| `RetryMaxDelay` | `30s` | Upper bound on any single backoff delay |
| `RedactPayload` | `true` | Mask common sensitive patterns before enqueue |
| `OnBeforeEnqueue` | — | Optional hook to transform events before enqueue |

The HTTP request timeout is fixed at **30s** per batch request.

## Retries and delivery

A failed batch flush is retried up to **`MaxRetries`** times with exponential backoff and equal
jitter (delay = `min(RetryMaxDelay, RetryBaseDelay × 2ⁿ)`, half fixed / half random), capped by
**`RetryMaxDelay`**.

Only **transient** failures are retried:

- network, I/O, and request-timeout errors
- HTTP **429** and **5xx**

Everything else (**4xx** other than 429 — bad auth, validation errors) fails immediately and is
reported through **`OnSendError`**; retrying would not help. Every event carries an
`IdempotencyKey`, so a retried batch **never duplicates events** server-side.

When all attempts are exhausted, the batch is dropped and the final exception goes to
**`OnSendError`**. Capture is fire-and-forget: no failure ever surfaces to the `CaptureEvent`
caller.

## Privacy and payload redaction

By default (`RedactPayload: true`), payloads are redacted before enqueue:

- String values are scanned for emails, phone numbers, credit cards, SSN-like values, JWTs, API keys, and URL-embedded passwords.
- `url`, `referrer`, and keys ending in `Url` have query strings and hashes stripped.
- Keys containing `password`, `secret`, `token`, `api_key`, `authorization`, `cookie`, `ssn`, or `credit_card` are replaced with `***`.

```csharp
ChurnWarn.Initialize(new ChurnWarnOptions
{
    BaseUrl = new Uri("https://your-gateway.example.com"),
    ApiKey = Environment.GetEnvironmentVariable("CHURNWARN_API_KEY"),
    RedactPayload = true, // default
    OnBeforeEnqueue = input => input, // optional
});
```

- **`PayloadJson`** is parsed and redacted when possible; if parsing fails, the raw string is masked. This is an advanced escape hatch — prefer **`Payload`** objects.
- Prefer **`path`** or **`route`** over full URLs in server-side payloads.
- Avoid using emails or usernames as `ExternalAccountId` when a stable non-PII id is available.

## Account facts — `UpsertAccountAsync`

Some dashboard-template signals are **slow-changing account facts**, not events: the fintech
`direct_deposit`/`kyc_completed` flags, a marketplace account's `role`, or a headline money
figure. Write them with **`UpsertAccountAsync`** (`PUT /api/accounts/{externalId}`). Unlike
`CaptureEvent`, it awaits the request and throws `ChurnWarnApiException` on a non-2xx response.
Only non-null fields are sent.

```csharp
await ChurnWarn.UpsertAccountAsync(new AccountUpsertInput("acct-1")
{
    BusinessType = BusinessTypes.Fintech,
    MonetaryValue = 2450.00m,
    ValueBasis = "balance",
    Role = "buyer", // marketplace side
    Attributes = new Dictionary<string, object?>
    {
        [AccountAttributes.DirectDeposit] = true,
        ["kyc_completed"] = true,
    },
});
```

Known properties map to account columns; `Attributes` merges into the fact bag (a `null` value
removes a key). Enum-ish fields (`Kind`, `ValueBasis`, `Status`, `Role`) are lowercased. Put
**metrics** in events, **facts** in `UpsertAccountAsync`.

## Constants

- **`Metrics`** — canonical metric strings (all business-type template signals; `Metrics.All` mirrors `DefaultDashboardMetricKeys.All`).
- **`RawEvents`** — dotted raw vendor names the gateway maps to `Metrics`.
- **`PayloadFields`** — payload keys read by `sum_payload`/`avg_payload` (`value`, `side`, `quantity`).
- **`AccountAttributes`** — account fact keys (`direct_deposit`, `kyc_completed`, `push_opt_in`, `installed_at`).
- **`BusinessTypes`** — dashboard-template keys (`ecommerce`, `fintech`, `subscription_box`, `mobile`, `marketplace_buyer`, …).

## Payload and idempotency

Use **`CaptureEvent(RecordEventInput)`** for full control. **`Payload`** is serialized to JSON; **`PayloadJson`** overrides when you already have a JSON string (advanced — redacted when parseable). If **`IdempotencyKey`** is omitted, a new key is generated per event at send time.
