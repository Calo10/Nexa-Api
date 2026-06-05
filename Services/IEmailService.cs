namespace NexaApi.Services;

public interface IEmailService
{
    Task SendMagicLinkAsync(
        string toEmail,
        string magicLinkToken,
        string? callbackUrl = null,
        CancellationToken cancellationToken = default);
    Task SendPasswordResetLinkAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default);
}

