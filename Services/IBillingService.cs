using NexaApi.DTOs.Billing;

namespace NexaApi.Services;

public interface IBillingService
{
    Task<CheckoutResponse> CreateCheckoutSessionAsync(Guid orgId, Guid userId, string planKey, string successUrl, string cancelUrl, CancellationToken cancellationToken = default);
    Task<SubscriptionDto?> GetSubscriptionAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task CancelAtPeriodEndAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task HandleStripeWebhookAsync(string jsonBody, string stripeSignatureHeader, CancellationToken cancellationToken = default);
}

