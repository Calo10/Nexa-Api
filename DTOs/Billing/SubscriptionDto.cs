namespace NexaApi.DTOs.Billing;

public class SubscriptionDto
{
    public string PlanKey { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
}

