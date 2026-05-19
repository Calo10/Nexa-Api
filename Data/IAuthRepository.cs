using System.Data;

namespace NexaApi.Data;

public interface IAuthRepository
{
    Task<UserRecord?> GetUserByEmailAsync(string emailNormalized, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Guid> CreateUserAsync(string email, string? fullName, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Guid> InsertMagicLinkAsync(string email, Guid? userId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<MagicLinkRecord?> GetValidMagicLinkByTokenHashAsync(string tokenHash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task ConsumeMagicLinkAsync(Guid magicLinkId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Guid> CreateSessionAsync(Guid userId, Guid? orgId, string refreshTokenHash, DateTimeOffset expiresAt, string? userAgent, string? ip, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<SessionRecord?> GetSessionByRefreshTokenHashAsync(string hash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task RotateSessionRefreshTokenAsync(Guid sessionId, string newHash, DateTimeOffset newExpiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task RevokeSessionAsync(Guid sessionId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task UpdateSessionOrgIdAsync(Guid sessionId, Guid? orgId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task IncrementFailedLoginCountAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task ResetFailedLoginCountAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task SetAccountLockoutAsync(Guid userId, DateTimeOffset lockedUntil, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetUserByIdAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task UpdatePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Guid> InsertPasswordResetTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<PasswordResetTokenRecord?> GetValidPasswordResetTokenByHashAsync(string tokenHash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task ConsumePasswordResetTokenAsync(Guid tokenId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
}

public record UserRecord(
    Guid Id,
    string Email,
    string EmailNormalized,
    string? FullName,
    string? AvatarUrl,
    string Status,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    string? PasswordHash,
    DateTimeOffset? PasswordSetAt,
    int FailedLoginCount,
    DateTimeOffset? LockedUntil
);

public record MagicLinkRecord(
    Guid Id,
    string Email,
    string EmailNormalized,
    Guid? UserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset CreatedAt
);

public record SessionRecord(
    Guid Id,
    Guid UserId,
    Guid? OrgId,
    string RefreshTokenHash,
    string? UserAgent,
    string? Ip,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt
);

public record PasswordResetTokenRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset CreatedAt
);
