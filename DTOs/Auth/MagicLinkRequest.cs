namespace NexaApi.DTOs.Auth;

public class MagicLinkRequest
{
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Optional frontend URL for the magic link (e.g. https://app.example.com/auth/callback).
    /// When omitted, Nexa uses Frontend:BaseUrl + /auth/verify.
    /// </summary>
    public string? CallbackUrl { get; set; }
}

