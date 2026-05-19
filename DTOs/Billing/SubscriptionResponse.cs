namespace NexaApi.DTOs.Billing;

public class SubscriptionResponse
{
    public string Status { get; set; } = string.Empty;
    public string? PlanId { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool IsActive { get; set; }
}

