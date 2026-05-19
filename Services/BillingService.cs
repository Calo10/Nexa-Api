using Dapper;
using Microsoft.Extensions.Configuration;
using NexaApi.Data;
using NexaApi.DTOs.Billing;
using Stripe;
using Stripe.Checkout;
using System.Data;

namespace NexaApi.Services;

public class BillingService : IBillingService
{
    private readonly BillingRepository _billingRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BillingService> _logger;

    public BillingService(
        BillingRepository billingRepository,
        IDbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<BillingService> logger)
    {
        _billingRepository = billingRepository;
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;

        // Configure Stripe API key
        var stripeSecretKey = _configuration["Stripe:SecretKey"];
        if (!string.IsNullOrEmpty(stripeSecretKey))
        {
            StripeConfiguration.ApiKey = stripeSecretKey;
        }
    }

    public async Task<CheckoutResponse> CreateCheckoutSessionAsync(
        Guid orgId,
        Guid userId,
        string planKey,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default)
    {
        // Normalize planKey
        planKey = planKey.ToLowerInvariant();

        // Validate planKey
        var validPlanKeys = new[] { "free", "starter", "growth", "pro" };
        if (!validPlanKeys.Contains(planKey))
        {
            throw new ArgumentException($"Invalid planKey. Must be one of: {string.Join(", ", validPlanKeys)}", nameof(planKey));
        }

        // Load the plan from database BEFORE any usage
        var plan = await _billingRepository.GetPlanByKeyAsync(planKey, cancellationToken);
        if (plan == null || !plan.IsActive)
        {
            throw new InvalidOperationException($"Plan with key '{planKey}' not found or is not active");
        }

        // Handle FREE plan IMMEDIATELY AFTER loading plan
        if (planKey == "free")
        {
            // Do NOT create Stripe customer
            // Do NOT create Stripe checkout session
            // Do NOT read Stripe price IDs
            // Call UpsertSubscriptionAsync directly
            await _billingRepository.UpsertSubscriptionAsync(
                orgId: orgId,
                planId: plan.Id,
                status: "active",
                stripeCustomerId: null,
                stripeSubscriptionId: null,
                currentPeriodStart: null,
                currentPeriodEnd: null,
                cancelAtPeriodEnd: false,
                canceledAt: null,
                transaction: null,
                cancellationToken: cancellationToken);

            return new CheckoutResponse
            {
                AlreadySubscribed = false,
                Message = "Free plan activated"
            };
        }

        // Only AFTER Free plan handling, process paid plans

        // Check existing subscription
        var existingSubscription = await _billingRepository.GetSubscriptionByOrgIdAsync(orgId, cancellationToken);
        if (existingSubscription != null)
        {
            var isActive = existingSubscription.Status == "active" || existingSubscription.Status == "trialing";
            var notCanceling = !existingSubscription.CancelAtPeriodEnd;

            if (isActive && notCanceling)
            {
                return new CheckoutResponse
                {
                    AlreadySubscribed = true,
                    Message = "Organization already has an active subscription"
                };
            }
        }

        // Get or create Stripe customer
        string? stripeCustomerId = null;
        if (existingSubscription != null && !string.IsNullOrEmpty(existingSubscription.StripeCustomerId))
        {
            stripeCustomerId = existingSubscription.StripeCustomerId;
        }
        else
        {
            // Create Stripe customer
            var customerService = new CustomerService();
            var customerOptions = new CustomerCreateOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "org_id", orgId.ToString() }
                }
            };

            var customer = await customerService.CreateAsync(customerOptions, cancellationToken: cancellationToken);
            stripeCustomerId = customer.Id;

            // Store customer ID in subscription if subscription exists
            if (existingSubscription != null)
            {
                using var connection = _connectionFactory.CreateConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                try
                {
                    const string updateSql = @"
                        UPDATE subscriptions
                        SET stripe_customer_id = @StripeCustomerId,
                            updated_at = @UpdatedAt
                        WHERE org_id = @OrgId";

                    await connection.ExecuteAsync(
                        new CommandDefinition(updateSql, new
                        {
                            OrgId = orgId,
                            StripeCustomerId = stripeCustomerId,
                            UpdatedAt = Sql.UtcNow
                        }, transaction, cancellationToken: cancellationToken));

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        // Get Stripe price ID from configuration
        var priceId = _configuration[$"Stripe:Prices:{planKey}"];
        if (string.IsNullOrEmpty(priceId))
        {
            throw new InvalidOperationException($"Stripe price ID not configured for plan '{planKey}'. Check appsettings.json Stripe:Prices:{planKey}");
        }

        // Create Stripe Checkout Session
        var sessionService = new SessionService();
        var sessionOptions = new SessionCreateOptions
        {
            Customer = stripeCustomerId,
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                { "org_id", orgId.ToString() },
                { "plan_key", planKey },
                { "user_id", userId.ToString() }
            }
        };

        var session = await sessionService.CreateAsync(sessionOptions, cancellationToken: cancellationToken);

        return new CheckoutResponse
        {
            CheckoutUrl = session.Url,
            SessionId = session.Id,
            AlreadySubscribed = false
        };
    }

    public async Task<SubscriptionDto?> GetSubscriptionAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        var subscriptionWithPlan = await _billingRepository.GetSubscriptionWithPlanByOrgIdAsync(orgId, cancellationToken);

        if (subscriptionWithPlan == null)
        {
            return null;
        }

        return new SubscriptionDto
        {
            PlanKey = subscriptionWithPlan.PlanKey,
            PlanName = subscriptionWithPlan.PlanName,
            PriceCents = subscriptionWithPlan.PriceCents,
            Status = subscriptionWithPlan.Status,
            CurrentPeriodStart = subscriptionWithPlan.CurrentPeriodStart,
            CurrentPeriodEnd = subscriptionWithPlan.CurrentPeriodEnd,
            CancelAtPeriodEnd = subscriptionWithPlan.CancelAtPeriodEnd
        };
    }

    public async Task CancelAtPeriodEndAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Update database
            await _billingRepository.UpdateCancelAtPeriodEndAsync(orgId, true, transaction, cancellationToken);

            // Get subscription to check if we have Stripe subscription ID
            var subscription = await _billingRepository.GetSubscriptionByOrgIdAsync(orgId, cancellationToken);
            if (subscription != null && !string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            {
                // Update Stripe subscription
                var subscriptionService = new SubscriptionService();
                var updateOptions = new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = true
                };

                await subscriptionService.UpdateAsync(subscription.StripeSubscriptionId, updateOptions, cancellationToken: cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task HandleStripeWebhookAsync(string jsonBody, string stripeSignatureHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jsonBody))
        {
            throw new InvalidOperationException("Webhook request body is empty");
        }

        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogError("Stripe:WebhookSecret is not configured in appsettings.json");
            throw new InvalidOperationException("Stripe:WebhookSecret is not configured. Please set it in appsettings.json");
        }

        // Log partial webhook secret for debugging (first 10 chars + last 4 chars)
        var secretPreview = webhookSecret.Length > 14 
            ? $"{webhookSecret.Substring(0, 10)}...{webhookSecret.Substring(webhookSecret.Length - 4)}"
            : "***";
        _logger.LogInformation("Using webhook secret: {SecretPreview}", secretPreview);

        // Check if webhook secret is still the placeholder
        if (webhookSecret == "whsec_REPLACE_WITH_STRIPE_CLI_SECRET" || 
            webhookSecret == "whsec_your_webhook_secret_here" || 
            !webhookSecret.StartsWith("whsec_"))
        {
            _logger.LogError(
                "Stripe:WebhookSecret is not configured correctly! " +
                "Current value: {SecretPreview}. " +
                "To fix: 1) Run 'stripe listen --forward-to localhost:5278/v1/billing/webhooks/stripe' " +
                "2) Copy the webhook secret shown (starts with whsec_) " +
                "3) Update appsettings.Development.json with: \"WebhookSecret\": \"whsec_...\" " +
                "4) Restart the application",
                secretPreview);
        }

        // Verify webhook signature
        Event stripeEvent;
        try
        {
            // Disable API version mismatch check for development (Stripe CLI uses older API version)
            // In production, ensure your webhook endpoint uses the latest API version
            stripeEvent = EventUtility.ConstructEvent(
                jsonBody, 
                stripeSignatureHeader, 
                webhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, 
                "Failed to verify Stripe webhook signature. " +
                "Current webhook secret in config: {SecretPreview}. " +
                "When using Stripe CLI, make sure the webhook secret in appsettings.json matches " +
                "the one shown when you run 'stripe listen --forward-to localhost:5278/v1/billing/webhooks/stripe'. " +
                "Each time you run 'stripe listen', it generates a NEW webhook secret that you must update in appsettings.json.",
                secretPreview);
            throw new UnauthorizedAccessException("Invalid Stripe webhook signature", ex);
        }

        _logger.LogInformation("Processing Stripe webhook event: {EventType} (ID: {EventId})", stripeEvent.Type, stripeEvent.Id);

        // Handle only specific events
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutSessionCompletedAsync(stripeEvent, cancellationToken);
                break;

            case "invoice.payment_succeeded":
                await HandleInvoicePaymentSucceededAsync(stripeEvent, cancellationToken);
                break;

            case "invoice.payment_failed":
                await HandleInvoicePaymentFailedAsync(stripeEvent, cancellationToken);
                break;

            case "customer.subscription.deleted":
                await HandleCustomerSubscriptionDeletedAsync(stripeEvent, cancellationToken);
                break;

            default:
                _logger.LogInformation("Ignoring webhook event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutSessionCompletedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            _logger.LogWarning("checkout.session.completed event data is not a Session object");
            return;
        }

        // Extract metadata
        if (session.Metadata == null || !session.Metadata.ContainsKey("org_id") || !session.Metadata.ContainsKey("plan_key"))
        {
            _logger.LogWarning(
                "checkout.session.completed event missing required metadata (org_id, plan_key). " +
                "This is normal for test events from Stripe CLI. " +
                "In production, ensure CreateCheckoutSessionAsync sets metadata correctly. " +
                "Session ID: {SessionId}",
                session.Id);
            return; // Silently ignore test events without metadata
        }

        if (!Guid.TryParse(session.Metadata["org_id"], out var orgId))
        {
            throw new InvalidOperationException($"Invalid org_id in metadata: {session.Metadata["org_id"]}");
        }

        var planKey = session.Metadata["plan_key"];
        var customerId = session.CustomerId;
        var subscriptionId = session.SubscriptionId;

        if (string.IsNullOrEmpty(customerId))
        {
            _logger.LogWarning("checkout.session.completed event missing customer_id. Session ID: {SessionId}", session.Id);
            return;
        }

        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogWarning("checkout.session.completed event missing subscription_id. Session ID: {SessionId}", session.Id);
            return;
        }

        // Get plan
        var plan = await _billingRepository.GetPlanByKeyAsync(planKey, cancellationToken);
        if (plan == null)
        {
            throw new InvalidOperationException($"Plan with key '{planKey}' not found");
        }

        // Upsert subscription
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await _billingRepository.UpsertSubscriptionAsync(
                orgId: orgId,
                planId: plan.Id,
                status: "active",
                stripeCustomerId: customerId,
                stripeSubscriptionId: subscriptionId,
                currentPeriodStart: null,
                currentPeriodEnd: null,
                cancelAtPeriodEnd: false,
                canceledAt: null,
                transaction: transaction,
                cancellationToken: cancellationToken);

            transaction.Commit();
            _logger.LogInformation("Subscription created/updated for org {OrgId} with plan {PlanKey}", orgId, planKey);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task HandleInvoicePaymentSucceededAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            _logger.LogWarning("invoice.payment_succeeded event data is not an Invoice object");
            return;
        }

        // Get subscription ID from the invoice
        // In Stripe.net, subscription is stored as a string ID in the raw JSON
        string? subscriptionId = null;
        
        // Access from raw JSON object
        if (invoice.RawJObject != null)
        {
            var subscriptionToken = invoice.RawJObject["subscription"];
            if (subscriptionToken != null)
            {
                // It can be a string ID or an expanded object
                if (subscriptionToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    subscriptionId = subscriptionToken.ToString();
                }
                else if (subscriptionToken["id"] != null)
                {
                    subscriptionId = subscriptionToken["id"]?.ToString();
                }
            }
        }

        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogWarning("invoice.payment_succeeded event missing subscription_id");
            return;
        }

        // Get subscription from database
        var subscription = await _billingRepository.GetSubscriptionByStripeSubscriptionIdAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription not found for Stripe subscription ID: {SubscriptionId}", subscriptionId);
            return;
        }

        // Update subscription
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await _billingRepository.UpdateSubscriptionStatusAsync(
                orgId: subscription.OrgId,
                status: "active",
                currentPeriodStart: invoice.PeriodStart,
                currentPeriodEnd: invoice.PeriodEnd,
                canceledAt: null, // Clear canceled_at if previously set
                cancelAtPeriodEnd: null, // Keep existing value
                transaction: transaction,
                cancellationToken: cancellationToken);

            transaction.Commit();
            _logger.LogInformation("Subscription updated for org {OrgId} after successful payment", subscription.OrgId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task HandleInvoicePaymentFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            _logger.LogWarning("invoice.payment_failed event data is not an Invoice object");
            return;
        }

        // Get subscription ID from the invoice
        // In Stripe.net, subscription is stored as a string ID in the raw JSON
        string? subscriptionId = null;
        
        // Access from raw JSON object
        if (invoice.RawJObject != null)
        {
            var subscriptionToken = invoice.RawJObject["subscription"];
            if (subscriptionToken != null)
            {
                // It can be a string ID or an expanded object
                if (subscriptionToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    subscriptionId = subscriptionToken.ToString();
                }
                else if (subscriptionToken["id"] != null)
                {
                    subscriptionId = subscriptionToken["id"]?.ToString();
                }
            }
        }

        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogWarning("invoice.payment_failed event missing subscription_id");
            return;
        }

        // Get subscription from database
        var subscription = await _billingRepository.GetSubscriptionByStripeSubscriptionIdAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription not found for Stripe subscription ID: {SubscriptionId}", subscriptionId);
            return;
        }

        // Update subscription status
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await _billingRepository.UpdateSubscriptionStatusAsync(
                orgId: subscription.OrgId,
                status: "past_due",
                currentPeriodStart: null,
                currentPeriodEnd: null,
                canceledAt: null,
                cancelAtPeriodEnd: null,
                transaction: transaction,
                cancellationToken: cancellationToken);

            transaction.Commit();
            _logger.LogInformation("Subscription status updated to past_due for org {OrgId}", subscription.OrgId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task HandleCustomerSubscriptionDeletedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSubscription == null)
        {
            _logger.LogWarning("customer.subscription.deleted event data is not a Subscription object");
            return;
        }

        var subscriptionId = stripeSubscription.Id;
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogWarning("customer.subscription.deleted event missing subscription id");
            return;
        }

        // Get subscription from database
        var subscription = await _billingRepository.GetSubscriptionByStripeSubscriptionIdAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription not found for Stripe subscription ID: {SubscriptionId}", subscriptionId);
            return;
        }

        // Update subscription
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await _billingRepository.UpdateSubscriptionStatusAsync(
                orgId: subscription.OrgId,
                status: "canceled",
                currentPeriodStart: null,
                currentPeriodEnd: null,
                canceledAt: Sql.UtcNow,
                cancelAtPeriodEnd: null,
                transaction: transaction,
                cancellationToken: cancellationToken);

            transaction.Commit();
            _logger.LogInformation("Subscription canceled for org {OrgId}", subscription.OrgId);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
