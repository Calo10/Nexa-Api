namespace NexaApi.DTOs.Invites;

public class SendInviteRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int ExpirationDays { get; set; } = 7;
}

