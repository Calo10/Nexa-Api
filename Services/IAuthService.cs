using NexaApi.DTOs.Auth;

namespace NexaApi.Services;

public interface IAuthService
{
    Task<MagicLinkResponse> SendMagicLinkAsync(string email, CancellationToken cancellationToken = default);
    Task<AuthResponse?> ConsumeTokenAsync(string token, string? userAgent, string? ip, CancellationToken cancellationToken = default);
    Task<AuthResponse?> LoginWithPasswordAsync(string email, string password, string? userAgent, string? ip, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task InitializePasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
    Task<AuthResponse?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<UserInfoResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class MagicLinkResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; } // Only in Development
    public DateTimeOffset ExpiresAt { get; set; }
}

