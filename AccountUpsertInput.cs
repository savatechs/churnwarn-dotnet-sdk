namespace ChurnWarn.Sdk;

/// <summary>
/// Slow-changing account facts written via <see cref="ChurnWarn.UpsertAccountAsync"/>
/// (PUT /api/accounts/{externalId}). Only non-null properties are sent; the endpoint
/// writes only the fields it receives. Use for attributes/flags and the commercial
/// block — not for time-series metrics, which belong in events.
/// </summary>
public sealed record AccountUpsertInput(string ExternalAccountId)
{
    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>Contact email.</summary>
    public string? Email { get; init; }

    /// <summary><c>company</c> (default) or <c>person</c>. See <see cref="AccountKinds"/>.</summary>
    public string? Kind { get; init; }

    /// <summary>Per-account override of the project business type. See <see cref="BusinessTypes"/>.</summary>
    public string? BusinessType { get; init; }

    /// <summary>Headline money figure; meaning set by <see cref="ValueBasis"/>.</summary>
    public decimal? MonetaryValue { get; init; }

    /// <summary><c>mrr</c> · <c>arr</c> · <c>ltv</c> · <c>balance</c> · <c>gmv</c> · <c>none</c>.</summary>
    public string? ValueBasis { get; init; }

    /// <summary>ISO currency code, e.g. <c>USD</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>Current plan key.</summary>
    public string? PlanKey { get; init; }

    /// <summary>Free-form lifecycle stage.</summary>
    public string? LifecycleStage { get; init; }

    /// <summary>Contract renewal date, where the model has one.</summary>
    public DateTimeOffset? RenewalAt { get; init; }

    /// <summary><c>active</c> · <c>atrisk</c> · <c>churned</c>.</summary>
    public string? Status { get; init; }

    /// <summary>Marketplace side: <c>buyer</c> or <c>seller</c>.</summary>
    public string? Role { get; init; }

    /// <summary>Merged into the account fact bag; a null value removes a key.</summary>
    public IReadOnlyDictionary<string, object?>? Attributes { get; init; }
}

/// <summary>Account classification values for <see cref="AccountUpsertInput.Kind"/>.</summary>
public static class AccountKinds
{
    public const string Company = "company";
    public const string Person = "person";
}
