using System.Data;
using Dapper;
using NexaApi.Data;
using NexaApi.DTOs.Invites;

namespace NexaApi.Services;

public class InvitesService : IInvitesService
{
    private readonly IOrgRepository _orgRepository;
    private readonly IAuthRepository _authRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InvitesService> _logger;

    public InvitesService(
        IOrgRepository orgRepository,
        IAuthRepository authRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<InvitesService> logger)
    {
        _orgRepository = orgRepository;
        _authRepository = authRepository;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<InviteResponse?> SendInviteAsync(Guid orgId, Guid inviterUserId, string email, string role, CancellationToken cancellationToken = default)
    {
        // Validate inviter role (owner/admin)
        var membership = await _orgRepository.GetMembershipAsync(orgId, inviterUserId, cancellationToken);
        if (membership == null)
        {
            _logger.LogWarning("User {UserId} attempted to send invite to org {OrgId} without membership", inviterUserId, orgId);
            return null;
        }

        if (membership.Role != "owner" && membership.Role != "admin")
        {
            _logger.LogWarning("User {UserId} attempted to send invite to org {OrgId} without owner/admin role", inviterUserId, orgId);
            return null;
        }

        // Normalize email
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Generate token + hash
        var rawToken = Sql.GenerateSecureToken();
        var tokenHash = Sql.HashToken(rawToken);

        // Calculate expiration (default 7 days)
        var expiresAt = Sql.UtcNow.AddDays(7);

        // Insert invite
        var inviteId = await _orgRepository.CreateInviteAsync(
            orgId,
            email,
            role,
            inviterUserId,
            tokenHash,
            expiresAt,
            cancellationToken: cancellationToken);

        // TODO: Send email with accept link containing raw token
        _logger.LogInformation("Invite created for {Email} to org {OrgId}. Token expires at {ExpiresAt}", email, orgId, expiresAt);

        // Get organization for response
        var org = await _orgRepository.GetOrganizationAsync(orgId, cancellationToken);
        if (org == null)
        {
            return null;
        }

        return new InviteResponse
        {
            InviteId = inviteId,
            Email = email,
            Role = role,
            ExpiresAt = expiresAt,
            CreatedAt = Sql.UtcNow,
            Status = "pending"
        };
    }

    public async Task<bool> AcceptInviteAsync(string token, CancellationToken cancellationToken = default)
    {
        // Validate token
        var tokenHash = Sql.HashToken(token);
        var invite = await _orgRepository.GetInviteByTokenHashAsync(tokenHash, cancellationToken);
        if (invite == null)
        {
            _logger.LogWarning("Invalid or expired invite token");
            return false;
        }

        // Check if already accepted
        if (invite.AcceptedAt.HasValue)
        {
            _logger.LogWarning("Invite {InviteId} has already been accepted", invite.Id);
            return false;
        }

        // Normalize email
        var normalizedEmail = invite.Email.ToLowerInvariant().Trim();

        // Create user if not exists
        var user = await _authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);
        Guid userId;

        if (user == null)
        {
            userId = await _authRepository.CreateUserAsync(invite.Email, null, cancellationToken: cancellationToken);
            user = await _authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);
            if (user == null)
            {
                _logger.LogError("Failed to create user for email {Email}", invite.Email);
                return false;
            }
        }
        else
        {
            userId = user.Id;
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("Attempted invite acceptance by disabled user {UserId}", userId);
            return false;
        }

        // Check if membership already exists
        var existingMembership = await _orgRepository.GetMembershipAsync(invite.OrgId, userId, cancellationToken);
        if (existingMembership != null && existingMembership.Status == "active")
        {
            _logger.LogWarning("User {UserId} already has active membership in org {OrgId}", userId, invite.OrgId);
            // Still mark invite as accepted
            await _orgRepository.AcceptInviteAsync(invite.Id, cancellationToken: cancellationToken);
            return true;
        }

        // Create org_membership in a transaction
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Create membership
            var membershipId = Guid.NewGuid();
            var now = Sql.UtcNow;

            const string sqlMembership = @"
                INSERT INTO org_memberships (id, org_id, user_id, role, status, joined_at, created_at)
                VALUES (@Id, @OrgId, @UserId, @Role, @Status, @JoinedAt, @CreatedAt)";

            await connection.ExecuteAsync(
                new CommandDefinition(sqlMembership, new
                {
                    Id = membershipId,
                    OrgId = invite.OrgId,
                    UserId = userId,
                    Role = invite.Role,
                    Status = "active",
                    JoinedAt = now,
                    CreatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            // Mark invite accepted
            var acceptedAt = Sql.UtcNow;
            const string sqlAccept = @"
                UPDATE org_invites
                SET accepted_at = @AcceptedAt
                WHERE id = @InviteId";

            await connection.ExecuteAsync(
                new CommandDefinition(sqlAccept, new { InviteId = invite.Id, AcceptedAt = acceptedAt }, transaction, cancellationToken: cancellationToken));

            transaction.Commit();
            _logger.LogInformation("Invite {InviteId} accepted, membership created for user {UserId} in org {OrgId}", invite.Id, userId, invite.OrgId);
            return true;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Failed to accept invite {InviteId}", invite.Id);
            throw;
        }
    }

    public async Task<bool> ResendInviteAsync(Guid orgId, Guid userId, Guid inviteId, CancellationToken cancellationToken = default)
    {
        // Validate user role (owner/admin)
        var membership = await _orgRepository.GetMembershipAsync(orgId, userId, cancellationToken);
        if (membership == null)
        {
            _logger.LogWarning("User {UserId} attempted to resend invite {InviteId} without membership", userId, inviteId);
            return false;
        }

        if (membership.Role != "owner" && membership.Role != "admin")
        {
            _logger.LogWarning("User {UserId} attempted to resend invite {InviteId} without owner/admin role", userId, inviteId);
            return false;
        }

        // Generate new token + hash
        var rawToken = Sql.GenerateSecureToken();
        var tokenHash = Sql.HashToken(rawToken);

        // Calculate new expiration
        var expiresAt = Sql.UtcNow.AddDays(7);

        // Resend invite
        await _orgRepository.ResendInviteAsync(inviteId, tokenHash, expiresAt, cancellationToken: cancellationToken);

        // TODO: Send email with new accept link containing raw token
        _logger.LogInformation("Invite {InviteId} resent. New token expires at {ExpiresAt}", inviteId, expiresAt);

        return true;
    }
}
