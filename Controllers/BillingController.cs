using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Billing;
using NexaApi.Services;
using System.Security.Claims;
using System.Text;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly ILogger<BillingController> _logger;

    public BillingController(IBillingService billingService, ILogger<BillingController> logger)
    {
        _billingService = billingService;
        _logger = logger;
    }

    [HttpGet("subscription")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SubscriptionDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscription(CancellationToken cancellationToken)
    {
        var orgId = GetActiveOrganizationId();

        if (orgId == null)
        {
            return BadRequest(new { error = "No active organization" });
        }

        var subscription = await _billingService.GetSubscriptionAsync(orgId.Value, cancellationToken);

        if (subscription == null)
        {
            return NotFound(new { error = "Subscription not found" });
        }

        return Ok(subscription);
    }

    [HttpPost("checkout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CheckoutResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        // Get userId from JWT claim
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        if (string.IsNullOrWhiteSpace(request.PlanId))
        {
            return BadRequest(new { error = "PlanId is required" });
        }

        var orgId = GetActiveOrganizationId();
        if (orgId == null)
        {
            return BadRequest(new { error = "No active organization" });
        }

        // Get success and cancel URLs from request or use defaults
        var successUrl = request.SuccessUrl ?? $"{Request.Scheme}://{Request.Host}/billing/success";
        var cancelUrl = request.CancelUrl ?? $"{Request.Scheme}://{Request.Host}/billing/cancel";

        try
        {
            var checkout = await _billingService.CreateCheckoutSessionAsync(
                orgId.Value,
                userId,
                request.PlanId,
                successUrl,
                cancelUrl,
                cancellationToken);

            return Ok(checkout);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("cancel")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelSubscription(CancellationToken cancellationToken)
    {
        var orgId = GetActiveOrganizationId();
        if (orgId == null)
        {
            return BadRequest(new { error = "No active organization" });
        }

        try
        {
            await _billingService.CancelAtPeriodEndAsync(orgId.Value, cancellationToken);
            return Ok(new { message = "Subscription will be cancelled at period end" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("webhooks/stripe")]
    [AllowAnonymous]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleStripeWebhook(CancellationToken cancellationToken)
    {
        // Stripe webhooks require reading the raw request body for signature verification
        // Enable buffering to read the body stream
        Request.EnableBuffering();
        Request.Body.Position = 0;

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        Request.Body.Position = 0;

        // Extract Stripe signature from header (Stripe doesn't use Authorization header)
        var signature = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Stripe webhook request missing Stripe-Signature header");
            return BadRequest(new { error = "Missing Stripe signature" });
        }

        if (string.IsNullOrEmpty(payload))
        {
            _logger.LogWarning("Stripe webhook request has empty body");
            return BadRequest(new { error = "Empty request body" });
        }

        try
        {
            await _billingService.HandleStripeWebhookAsync(payload, signature, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook signature");
            return Unauthorized(new { error = "Invalid webhook signature" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing Stripe webhook");
            return StatusCode(500, new { error = "Internal server error processing webhook" });
        }
    }

    private Guid? GetActiveOrganizationId()
    {
        // Get org_id from JWT claim
        var orgIdClaim = User.FindFirst("org_id")?.Value;
        if (Guid.TryParse(orgIdClaim, out var orgId))
        {
            return orgId;
        }
        return null;
    }
}

