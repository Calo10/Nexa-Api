using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace NexaApi.Data;

public class AuthRepository : IAuthRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AuthRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<UserRecord?> GetUserByEmailAsync(string emailNormalized, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                email AS Email,
                email_normalized AS EmailNormalized,
                full_name AS FullName,
                avatar_url AS AvatarUrl,
                status AS Status,
                email_verified_at AS EmailVerifiedAt,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt,
                deleted_at AS DeletedAt,
                password_hash AS PasswordHash,
                password_set_at AS PasswordSetAt,
                failed_login_count AS FailedLoginCount,
                locked_until AS LockedUntil
            FROM users
            WHERE email_normalized = @EmailNormalized
              AND deleted_at IS NULL";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<UserRecord>(
                new CommandDefinition(sql, new { EmailNormalized = emailNormalized }, transaction, cancellationToken: cancellationToken));
            return result;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<Guid> CreateUserAsync(string email, string? fullName, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sql = @"
            INSERT INTO users (id, email, full_name, status, created_at, updated_at)
            VALUES (@Id, @Email, @FullName, @Status, @CreatedAt, @UpdatedAt)";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    Email = email,
                    FullName = fullName,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            return id;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<Guid> InsertMagicLinkAsync(string email, Guid? userId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sql = @"
            INSERT INTO magic_links (id, email, user_id, token_hash, expires_at, created_at)
            VALUES (@Id, @Email, @UserId, @TokenHash, @ExpiresAt, @CreatedAt)";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    Email = email,
                    UserId = userId,
                    TokenHash = tokenHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            return id;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<MagicLinkRecord?> GetValidMagicLinkByTokenHashAsync(string tokenHash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            SELECT 
                id AS Id,
                email AS Email,
                email_normalized AS EmailNormalized,
                user_id AS UserId,
                token_hash AS TokenHash,
                expires_at AS ExpiresAt,
                consumed_at AS ConsumedAt,
                created_at AS CreatedAt
            FROM magic_links
            WHERE token_hash = @TokenHash
              AND expires_at > @Now
              AND consumed_at IS NULL";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<MagicLinkRecord>(
                new CommandDefinition(sql, new { TokenHash = tokenHash, Now = now }, transaction, cancellationToken: cancellationToken));
            return result;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task ConsumeMagicLinkAsync(Guid magicLinkId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            UPDATE magic_links
            SET consumed_at = @ConsumedAt
            WHERE id = @Id";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { Id = magicLinkId, ConsumedAt = now }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<Guid> CreateSessionAsync(Guid userId, Guid? orgId, string refreshTokenHash, DateTimeOffset expiresAt, string? userAgent, string? ip, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sql = @"
            INSERT INTO sessions (id, user_id, org_id, refresh_token_hash, user_agent, ip, expires_at, created_at)
            VALUES (@Id, @UserId, @OrgId, @RefreshTokenHash, @UserAgent, @Ip, @ExpiresAt, @CreatedAt)";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    UserId = userId,
                    OrgId = orgId,
                    RefreshTokenHash = refreshTokenHash,
                    UserAgent = userAgent,
                    Ip = ip,
                    ExpiresAt = expiresAt,
                    CreatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            return id;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<SessionRecord?> GetSessionByRefreshTokenHashAsync(string hash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            SELECT 
                id AS Id,
                user_id AS UserId,
                org_id AS OrgId,
                refresh_token_hash AS RefreshTokenHash,
                user_agent AS UserAgent,
                ip AS Ip,
                expires_at AS ExpiresAt,
                revoked_at AS RevokedAt,
                created_at AS CreatedAt
            FROM sessions
            WHERE refresh_token_hash = @Hash
              AND revoked_at IS NULL
              AND expires_at > @Now";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<SessionRecord>(
                new CommandDefinition(sql, new { Hash = hash, Now = now }, transaction, cancellationToken: cancellationToken));
            return result;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task RotateSessionRefreshTokenAsync(Guid sessionId, string newHash, DateTimeOffset newExpiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE sessions
            SET refresh_token_hash = @NewHash, expires_at = @NewExpiresAt
            WHERE id = @SessionId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    SessionId = sessionId,
                    NewHash = newHash,
                    NewExpiresAt = newExpiresAt
                }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task RevokeSessionAsync(Guid sessionId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            UPDATE sessions
            SET revoked_at = @RevokedAt
            WHERE id = @SessionId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { SessionId = sessionId, RevokedAt = now }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task UpdateSessionOrgIdAsync(Guid sessionId, Guid? orgId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE sessions
            SET org_id = @OrgId
            WHERE id = @SessionId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { SessionId = sessionId, OrgId = orgId }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task IncrementFailedLoginCountAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE users
            SET failed_login_count = ISNULL(failed_login_count, 0) + 1,
                updated_at = @UpdatedAt
            WHERE id = @UserId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { UserId = userId, UpdatedAt = Sql.UtcNow }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task ResetFailedLoginCountAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE users
            SET failed_login_count = 0,
                locked_until = NULL,
                updated_at = @UpdatedAt
            WHERE id = @UserId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { UserId = userId, UpdatedAt = Sql.UtcNow }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task SetAccountLockoutAsync(Guid userId, DateTimeOffset lockedUntil, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE users
            SET locked_until = @LockedUntil,
                updated_at = @UpdatedAt
            WHERE id = @UserId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { UserId = userId, LockedUntil = lockedUntil, UpdatedAt = Sql.UtcNow }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<UserRecord?> GetUserByIdAsync(Guid userId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                email AS Email,
                email_normalized AS EmailNormalized,
                full_name AS FullName,
                avatar_url AS AvatarUrl,
                status AS Status,
                email_verified_at AS EmailVerifiedAt,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt,
                deleted_at AS DeletedAt,
                password_hash AS PasswordHash,
                password_set_at AS PasswordSetAt,
                failed_login_count AS FailedLoginCount,
                locked_until AS LockedUntil
            FROM users
            WHERE id = @UserId
              AND deleted_at IS NULL";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<UserRecord>(
                new CommandDefinition(sql, new { UserId = userId }, transaction, cancellationToken: cancellationToken));
            return result;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task UpdatePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE users
            SET password_hash = @PasswordHash,
                password_set_at = @PasswordSetAt,
                updated_at = @UpdatedAt,
                failed_login_count = 0,
                locked_until = NULL
            WHERE id = @UserId
              AND deleted_at IS NULL";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new 
                { 
                    UserId = userId, 
                    PasswordHash = passwordHash,
                    PasswordSetAt = now,
                    UpdatedAt = now
                }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<Guid> InsertPasswordResetTokenAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sql = @"
            INSERT INTO password_reset_tokens (id, user_id, token_hash, expires_at, created_at)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt, @CreatedAt)";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    UserId = userId,
                    TokenHash = tokenHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            return id;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<PasswordResetTokenRecord?> GetValidPasswordResetTokenByHashAsync(string tokenHash, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            SELECT 
                id AS Id,
                user_id AS UserId,
                token_hash AS TokenHash,
                expires_at AS ExpiresAt,
                consumed_at AS ConsumedAt,
                created_at AS CreatedAt
            FROM password_reset_tokens
            WHERE token_hash = @TokenHash
              AND expires_at > @Now
              AND consumed_at IS NULL";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<PasswordResetTokenRecord>(
                new CommandDefinition(sql, new { TokenHash = tokenHash, Now = now }, transaction, cancellationToken: cancellationToken));
            return result;
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task ConsumePasswordResetTokenAsync(Guid tokenId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            UPDATE password_reset_tokens
            SET consumed_at = @ConsumedAt
            WHERE id = @TokenId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { TokenId = tokenId, ConsumedAt = now }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }
}

