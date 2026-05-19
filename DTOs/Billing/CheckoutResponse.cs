namespace NexaApi.DTOs.Billing;

public class CheckoutResponse
{
    public string CheckoutUrl { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool AlreadySubscribed { get; set; }
    public string? Message { get; set; }
}

