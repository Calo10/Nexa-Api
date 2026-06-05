using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace NexaApi.Data;

public class OrgRepository : IOrgRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrgRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateOrganizationAsync(string name, string slug, string timezone, Guid ownerUserId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var orgId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sqlOrg = @"
            INSERT INTO organizations (id, name, slug, status, created_at, updated_at)
            VALUES (@Id, @Name, @Slug, @Status, @CreatedAt, @UpdatedAt)";

        const string sqlSettings = @"
            INSERT INTO org_settings (org_id, timezone)
            VALUES (@OrgId, @Timezone)";

        const string sqlMembership = @"
            INSERT INTO org_memberships (id, org_id, user_id, role, status, joined_at, created_at)
            VALUES (@Id, @OrgId, @UserId, @Role, @Status, @JoinedAt, @CreatedAt)";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        var shouldClose = false;

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            shouldClose = true;
        }

        var localTransaction = transaction;
        if (localTransaction == null)
        {
            localTransaction = connection.BeginTransaction();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sqlOrg, new
                {
                    Id = orgId,
                    Name = name,
                    Slug = slug,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                }, localTransaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(sqlSettings, new
                {
                    OrgId = orgId,
                    Timezone = timezone
                }, localTransaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(sqlMembership, new
                {
                    Id = membershipId,
                    OrgId = orgId,
                    UserId = ownerUserId,
                    Role = "owner",
                    Status = "active",
                    JoinedAt = now,
                    CreatedAt = now
                }, localTransaction, cancellationToken: cancellationToken));

            if (transaction == null)
            {
                localTransaction.Commit();
            }

            return orgId;
        }
        catch
        {
            if (transaction == null)
            {
                localTransaction.Rollback();
            }
            throw;
        }
        finally
        {
            if (transaction == null && localTransaction != null)
            {
                localTransaction.Dispose();
            }
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<List<OrganizationRecord>> ListOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT DISTINCT 
                o.id AS Id,
                o.name AS Name,
                o.slug AS Slug,
                o.status AS Status,
                o.created_at AS CreatedAt,
                o.updated_at AS UpdatedAt,
                o.deleted_at AS DeletedAt
            FROM organizations o
            INNER JOIN org_memberships m ON o.id = m.org_id
            WHERE m.user_id = @UserId
              AND m.status = 'active'
              AND o.deleted_at IS NULL
            ORDER BY o.created_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = await connection.QueryAsync<OrganizationRecord>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));

        return results.ToList();
    }

    public async Task<OrganizationRecord?> GetOrganizationAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                name AS Name,
                slug AS Slug,
                status AS Status,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt,
                deleted_at AS DeletedAt
            FROM organizations
            WHERE id = @OrgId
              AND deleted_at IS NULL";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<OrganizationRecord>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task<MembershipRecord?> GetMembershipAsync(Guid orgId, Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                org_id AS OrgId,
                user_id AS UserId,
                role AS Role,
                status AS Status,
                joined_at AS JoinedAt,
                created_at AS CreatedAt
            FROM org_memberships
            WHERE org_id = @OrgId
              AND user_id = @UserId
              AND status = 'active'";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<MembershipRecord>(
            new CommandDefinition(sql, new { OrgId = orgId, UserId = userId }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task<List<MemberRecord>> ListMembersAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                m.id AS Id,
                m.org_id AS OrgId,
                m.user_id AS UserId,
                u.email AS Email,
                u.full_name AS FullName,
                m.role AS Role,
                m.status AS Status,
                m.joined_at AS JoinedAt
            FROM org_memberships m
            INNER JOIN users u ON m.user_id = u.id
            WHERE m.org_id = @OrgId
              AND m.status = 'active'
              AND u.deleted_at IS NULL
            ORDER BY m.joined_at ASC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = await connection.QueryAsync<MemberRecord>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return results.ToList();
    }

    public async Task ChangeMemberRoleAsync(Guid orgId, Guid memberId, string newRole, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE org_memberships
            SET role = @NewRole
            WHERE id = @MemberId
              AND org_id = @OrgId
              AND status = 'active'";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { MemberId = memberId, OrgId = orgId, NewRole = newRole }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task RemoveMemberAsync(Guid orgId, Guid memberId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE org_memberships
            SET status = 'removed'
            WHERE id = @MemberId
              AND org_id = @OrgId
              AND status = 'active'";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { MemberId = memberId, OrgId = orgId }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<int> CountOwnersAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM org_memberships
            WHERE org_id = @OrgId
              AND role = 'owner'
              AND status = 'active'";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var count = await connection.QuerySingleAsync<int>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return count;
    }

    public async Task<Guid> CreateInviteAsync(Guid orgId, string email, string role, Guid invitedByUserId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var now = Sql.UtcNow;

        const string sql = @"
            INSERT INTO org_invites (id, org_id, email, role, invited_by_user_id, token_hash, expires_at, created_at)
            VALUES (@Id, @OrgId, @Email, @Role, @InvitedByUserId, @TokenHash, @ExpiresAt, @CreatedAt)";

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
                    OrgId = orgId,
                    Email = email,
                    Role = role,
                    InvitedByUserId = invitedByUserId,
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

    public async Task<InviteRecord?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            SELECT 
                id AS Id,
                org_id AS OrgId,
                email AS Email,
                email_normalized AS EmailNormalized,
                role AS Role,
                invited_by_user_id AS InvitedByUserId,
                token_hash AS TokenHash,
                expires_at AS ExpiresAt,
                accepted_at AS AcceptedAt,
                created_at AS CreatedAt
            FROM org_invites
            WHERE token_hash = @TokenHash
              AND expires_at > @Now
              AND accepted_at IS NULL";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<InviteRecord>(
            new CommandDefinition(sql, new { TokenHash = tokenHash, Now = now }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task<List<InviteRecord>> ListPendingInvitesByOrgAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            SELECT
                id AS Id,
                org_id AS OrgId,
                email AS Email,
                email_normalized AS EmailNormalized,
                role AS Role,
                invited_by_user_id AS InvitedByUserId,
                token_hash AS TokenHash,
                expires_at AS ExpiresAt,
                accepted_at AS AcceptedAt,
                created_at AS CreatedAt
            FROM org_invites
            WHERE org_id = @OrgId
              AND accepted_at IS NULL
              AND expires_at > @Now
            ORDER BY created_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryAsync<InviteRecord>(
            new CommandDefinition(sql, new { OrgId = orgId, Now = now }, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<List<InviteRecord>> ListPendingInvitesByEmailAsync(string emailNormalized, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;
        var normalized = emailNormalized.ToLowerInvariant().Trim();

        const string sql = @"
            SELECT
                id AS Id,
                org_id AS OrgId,
                email AS Email,
                email_normalized AS EmailNormalized,
                role AS Role,
                invited_by_user_id AS InvitedByUserId,
                token_hash AS TokenHash,
                expires_at AS ExpiresAt,
                accepted_at AS AcceptedAt,
                created_at AS CreatedAt
            FROM org_invites
            WHERE email_normalized = @EmailNormalized
              AND accepted_at IS NULL
              AND expires_at > @Now
            ORDER BY created_at DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryAsync<InviteRecord>(
            new CommandDefinition(sql, new { EmailNormalized = normalized, Now = now }, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<bool> RevokePendingInviteAsync(Guid orgId, Guid inviteId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM org_invites
            WHERE org_id = @OrgId
              AND id = @InviteId
              AND accepted_at IS NULL;";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { OrgId = orgId, InviteId = inviteId }, cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task AcceptInviteAsync(Guid inviteId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var now = Sql.UtcNow;

        const string sql = @"
            UPDATE org_invites
            SET accepted_at = @AcceptedAt
            WHERE id = @InviteId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { InviteId = inviteId, AcceptedAt = now }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task ResendInviteAsync(Guid inviteId, string newTokenHash, DateTimeOffset newExpiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE org_invites
            SET token_hash = @NewTokenHash, expires_at = @NewExpiresAt
            WHERE id = @InviteId
              AND accepted_at IS NULL";

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
                    InviteId = inviteId,
                    NewTokenHash = newTokenHash,
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
}

