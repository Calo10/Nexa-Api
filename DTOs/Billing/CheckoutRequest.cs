namespace NexaApi.DTOs.Billing;

public class CheckoutRequest
{
    public string PlanId { get; set; } = string.Empty;
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

