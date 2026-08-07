namespace ChurnWarn.Sdk;

/// <summary>Canonical metric keys (mirror backend DefaultDashboardMetricKeys.All / sdks/signals.manifest.json).</summary>
public static class Metrics
{
    // Core (SaaS / PLG)
    public const string Login = "login";
    public const string Session = "session";
    public const string FeatureUsed = "feature_used";
    public const string SeatUsed = "seat_used";
    public const string SeatPurchased = "seat_purchased";
    public const string ActiveUser = "active_user";
    public const string OnboardingCompleted = "onboarding_completed";
    public const string SupportTicketOpened = "support_ticket_opened";
    public const string SupportTicketResolved = "support_ticket_resolved";
    public const string SupportTicketNegative = "support_ticket_negative";
    public const string NpsResponse = "nps_response";
    public const string CsatResponse = "csat_response";
    public const string SeatExpanded = "seat_expanded";
    public const string PlanUpgraded = "plan_upgraded";
    public const string Referral = "referral";
    public const string FrustrationScore = "frustration_score";
    public const string ErrorRate = "error_rate";

    // E-commerce / RFM
    public const string OrderPlaced = "order_placed";
    public const string CartCreated = "cart_created";
    public const string CartAbandoned = "cart_abandoned";
    public const string ProductViewed = "product_viewed";
    public const string OrderReturned = "order_returned";

    // Fintech / neobank
    public const string CardTransaction = "card_transaction";
    public const string BillPay = "bill_pay";
    public const string TransactionDeclined = "transaction_declined";

    // Subscription-box
    public const string SubscriptionSkipped = "subscription_skipped";
    public const string SubscriptionPaused = "subscription_paused";
    public const string SubscriptionResumed = "subscription_resumed";
    public const string PaymentFailed = "payment_failed";
    public const string DeliveryIssue = "delivery_issue";

    // Mobile consumer app
    public const string AppOpen = "app_open";
    public const string AppInstalled = "app_installed";
    public const string AppUninstalled = "app_uninstalled";
    public const string PushEnabled = "push_enabled";
    public const string PushOpened = "push_opened";
    public const string IapPurchase = "iap_purchase";
    public const string PaywallView = "paywall_view";

    // Marketplace (two-sided)
    public const string Transaction = "transaction";
    public const string SearchPerformed = "search_performed";
    public const string ListingCreated = "listing_created";
    public const string TransactionRequest = "transaction_request";

    /// <summary>All canonical metric keys, for parity checks against the backend.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Login, Session, FeatureUsed, SeatUsed, SeatPurchased, ActiveUser, OnboardingCompleted,
        SupportTicketOpened, SupportTicketResolved, SupportTicketNegative, NpsResponse, CsatResponse,
        SeatExpanded, PlanUpgraded, Referral, FrustrationScore, ErrorRate,
        OrderPlaced, CartCreated, CartAbandoned, ProductViewed, OrderReturned,
        CardTransaction, BillPay, TransactionDeclined,
        SubscriptionSkipped, SubscriptionPaused, SubscriptionResumed, PaymentFailed, DeliveryIssue,
        AppOpen, AppInstalled, AppUninstalled, PushEnabled, PushOpened, IapPurchase, PaywallView,
        Transaction, SearchPerformed, ListingCreated, TransactionRequest,
    };
}

/// <summary>Raw event strings (common vendor names the gateway maps to canonical Metrics).</summary>
public static class RawEvents
{
    public const string AppLogin = "app.login";
    public const string AppSessionEnd = "app.session.end";
    public const string AppFeatureUsed = "app.feature.used";
    public const string BillingSeatsUsed = "billing.seats.used";
    public const string BillingSeatsPurchased = "billing.seats.purchased";
    public const string AppUserActive = "app.user.active";
    public const string AppOnboardingCompleted = "app.onboarding.completed";
    public const string SupportTicketOpened = "support.ticket.opened";
    public const string SupportTicketResolved = "support.ticket.resolved";
    public const string SupportTicketNegative = "support.ticket.negative";
    public const string SurveyNps = "survey.nps";
    public const string SurveyCsat = "survey.csat";
    public const string BillingSeatExpanded = "billing.seat.expanded";
    public const string BillingPlanUpgraded = "billing.plan.upgraded";
    public const string GrowthReferral = "growth.referral";

    // Tier-C vendor aliases
    public const string OrderPlaced = "order.placed";
    public const string OrderCompleted = "order.completed";
    public const string CheckoutCompleted = "checkout.completed";
    public const string CartAbandoned = "cart.abandoned";
    public const string ProductViewed = "product.viewed";
    public const string OrderRefunded = "order.refunded";
    public const string CardTransaction = "card.transaction";
    public const string BillPaid = "bill.paid";
    public const string PaymentDeclined = "payment.declined";
    public const string SubscriptionSkipped = "subscription.skipped";
    public const string PaymentFailed = "payment.failed";
    public const string DeliveryIssue = "delivery.issue";
    public const string AppOpen = "app.open";
    public const string IapPurchase = "iap.purchase";
    public const string PaywallView = "paywall.view";
    public const string TransactionCompleted = "transaction.completed";
    public const string SearchPerformed = "search.performed";
    public const string ListingCreated = "listing.created";
}

/// <summary>Numeric/dimension payload keys read by sum_payload / avg_payload and the marketplace side split.</summary>
public static class PayloadFields
{
    public const string Value = "value";
    public const string Side = "side";
    public const string Quantity = "quantity";
}

/// <summary>Slow-changing account facts set via <see cref="ChurnWarn.UpsertAccountAsync"/> → PUT /api/accounts.</summary>
public static class AccountAttributes
{
    public const string DirectDeposit = "direct_deposit";
    public const string KycCompleted = "kyc_completed";
    public const string PushOptIn = "push_opt_in";
    public const string InstalledAt = "installed_at";
}

/// <summary>Dashboard-template / business-type keys (mirror backend DashboardTemplateCatalog.BusinessTypes).</summary>
public static class BusinessTypes
{
    public const string B2bSaasSalesLed = "b2b_saas_salesled";
    public const string B2bSaasPlg = "b2b_saas_plg";
    public const string InternalTool = "internal_tool";
    public const string Ecommerce = "ecommerce";
    public const string Fintech = "fintech";
    public const string SubscriptionBox = "subscription_box";
    public const string Mobile = "mobile";
    public const string MarketplaceBuyer = "marketplace_buyer";
    public const string MarketplaceSeller = "marketplace_seller";
}
