namespace NexaApi.Services;

public interface IEmailService
{
    Task SendMagicLinkAsync(string toEmail, string magicLinkToken, CancellationToken cancellationToken = default);
    Task SendPasswordResetLinkAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default);
}

