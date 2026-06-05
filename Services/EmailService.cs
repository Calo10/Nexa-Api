using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net.Http.Json;

namespace NexaApi.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly string? _fromName;
    private readonly string? _frontendUrl;
    private readonly HttpClient _httpClient;
    private readonly string? _functionUrl;
    private readonly string? _functionCode;
    private readonly string _tenantId;

    public EmailService(HttpClient httpClient, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        // Azure Function (Nexa Messenger) configuration
        _functionUrl = _configuration["MessengerFunction:Url"]
            ?? "https://nexa-messenger-function.azurewebsites.net/api/messages/email";
        _functionCode = _configuration["MessengerFunction:Code"];
        // Match the sample curl default unless overridden.
        _tenantId = _configuration["MessengerFunction:TenantId"] ?? "tenant-a";

        _fromName = _configuration["MessengerFunction:FromName"]
            ?? "Nexa";

        // Frontend URL where the magic link will redirect (different from API URL)
        _frontendUrl = _configuration["Frontend:BaseUrl"]
            ?? _configuration["AppSettings:FrontendUrl"]
            ?? "http://localhost:5173"; // Default frontend port (Vite)
    }

    public async Task SendMagicLinkAsync(
        string toEmail,
        string magicLinkToken,
        string? callbackUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Build magic link URL - points to frontend that will call the API
        // The frontend will extract the token and call POST /v1/auth/consume
        var magicLinkUrl = BuildMagicLinkUrl(magicLinkToken, callbackUrl);

        var subject = "Sign in to Nexa";
        var plainTextContent = $"Click this link to sign in to Nexa: {magicLinkUrl}\n\nThis link will expire in 15 minutes.";
        // Inline styles to match the curl sample (safer across email clients).
        var htmlContent =
            $"""<html><head><meta charset="utf-8"></head><body style="font-family:Arial;line-height:1.6;color:#333;"><div style="max-width:600px;margin:0 auto;padding:20px;"><h2>Sign in to Nexa</h2><p>Click the button below to sign in:</p><a href="{magicLinkUrl}" style="display:inline-block;padding:12px 24px;background:#007bff;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0;">Sign In</a><p style="word-break:break-all;color:#666;">{magicLinkUrl}</p><p><b>This link expires in 15 minutes.</b></p><p style="margin-top:24px;font-size:12px;color:#666;border-top:1px solid #eee;padding-top:16px;">If you didn’t request this, ignore this email.</p></div></body></html>""";

        await SendEmailViaMessengerFunctionAsync(
            toEmail,
            subject,
            plainTextContent,
            htmlContent,
            emailType: "magic-link",
            cancellationToken: cancellationToken
        );
    }

    public async Task SendPasswordResetLinkAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        // Build password reset URL - points to frontend
        var resetUrl = $"{_frontendUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var subject = "Reset your Nexa password";
        var plainTextContent = $"Click this link to reset your password: {resetUrl}\n\nThis link will expire in 30 minutes.\n\nIf you didn't request this, you can safely ignore this email.";
        var htmlContent =
            $"""<html><head><meta charset="utf-8"></head><body style="font-family:Arial;line-height:1.6;color:#333;"><div style="max-width:600px;margin:0 auto;padding:20px;"><h2>Reset your password</h2><p>We received a request to reset your password. Click the button below to create a new password:</p><a href="{resetUrl}" style="display:inline-block;padding:12px 24px;background:#007bff;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0;">Reset Password</a><p style="word-break:break-all;color:#666;">{resetUrl}</p><p><b>This link expires in 30 minutes.</b></p><p style="margin-top:24px;font-size:12px;color:#666;border-top:1px solid #eee;padding-top:16px;">If you didn’t request this, ignore this email.</p></div></body></html>""";

        await SendEmailViaMessengerFunctionAsync(
            toEmail,
            subject,
            plainTextContent,
            htmlContent,
            emailType: "password-reset",
            cancellationToken: cancellationToken
        );
    }

    private string BuildMagicLinkUrl(string magicLinkToken, string? callbackUrl)
    {
        var tokenParam = $"token={Uri.EscapeDataString(magicLinkToken)}";
        if (!string.IsNullOrWhiteSpace(callbackUrl))
        {
            var baseUrl = callbackUrl.Trim();
            return baseUrl.Contains('?', StringComparison.Ordinal)
                ? $"{baseUrl}&{tokenParam}"
                : $"{baseUrl}?{tokenParam}";
        }

        return $"{_frontendUrl}/auth/verify?{tokenParam}";
    }

    private async Task SendEmailViaMessengerFunctionAsync(
        string toEmail,
        string subject,
        string plainText,
        string html,
        string emailType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_functionUrl) || string.IsNullOrWhiteSpace(_functionCode))
        {
            _logger.LogWarning(
                "MessengerFunction URL/code not configured. Email will not be sent. Type: {EmailType}, To: {Email}",
                emailType,
                toEmail
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(_tenantId))
        {
            _logger.LogWarning(
                "MessengerFunction tenantId not configured. Email will not be sent. Type: {EmailType}, To: {Email}",
                emailType,
                toEmail
            );
            return;
        }

        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var requestUri = $"{_functionUrl}?code={Uri.EscapeDataString(_functionCode)}";

        // Match the function contract from the provided curl sample.
        var payload = new
        {
            tenantId = _tenantId,
            correlationId,
            fromName = _fromName ?? "Nexa",
            to = new[] { toEmail },
            subject,
            plainText,
            html
        };

        try
        {
            _logger.LogInformation(
                "Sending email via MessengerFunction. Type: {EmailType}, TenantId: {TenantId}, To: {Email}, Subject: {Subject}, HtmlLength: {HtmlLength}, CorrelationId: {CorrelationId}",
                emailType,
                _tenantId,
                toEmail,
                subject,
                html?.Length ?? 0,
                correlationId
            );

            using var response = await _httpClient.PostAsJsonAsync(requestUri, payload, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Email sent via MessengerFunction. Type: {EmailType}, To: {Email}, Status: {StatusCode}, Response: {ResponseBody}, CorrelationId: {CorrelationId}",
                    emailType,
                    toEmail,
                    (int)response.StatusCode,
                    responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody,
                    correlationId
                );
                return;
            }

            _logger.LogError(
                "Failed to send email via MessengerFunction. Type: {EmailType}, To: {Email}, Status: {StatusCode}, Response: {ResponseBody}, CorrelationId: {CorrelationId}",
                emailType,
                toEmail,
                (int)response.StatusCode,
                responseBody,
                correlationId
            );

            throw new InvalidOperationException($"Failed to send email via MessengerFunction: {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error sending email via MessengerFunction. Type: {EmailType}, To: {Email}, CorrelationId: {CorrelationId}",
                emailType,
                toEmail,
                correlationId
            );
            throw;
        }
    }
}

