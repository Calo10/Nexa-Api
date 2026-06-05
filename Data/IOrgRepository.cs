using System.Data;

namespace NexaApi.Data;

public interface IOrgRepository
{
    // Organizations
    Task<Guid> CreateOrganizationAsync(string name, string slug, string timezone, Guid ownerUserId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<List<OrganizationRecord>> ListOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<OrganizationRecord?> GetOrganizationAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task<MembershipRecord?> GetMembershipAsync(Guid orgId, Guid userId, CancellationToken cancellationToken = default);

    // Members
    Task<List<MemberRecord>> ListMembersAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task ChangeMemberRoleAsync(Guid orgId, Guid memberId, string newRole, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid orgId, Guid memberId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<int> CountOwnersAsync(Guid orgId, CancellationToken cancellationToken = default);

    // Invites
    Task<Guid> CreateInviteAsync(Guid orgId, string email, string role, Guid invitedByUserId, string tokenHash, DateTimeOffset expiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<InviteRecord?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<List<InviteRecord>> ListPendingInvitesByOrgAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task AcceptInviteAsync(Guid inviteId, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task ResendInviteAsync(Guid inviteId, string newTokenHash, DateTimeOffset newExpiresAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<bool> RevokePendingInviteAsync(Guid orgId, Guid inviteId, CancellationToken cancellationToken = default);
}

public record OrganizationRecord(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt
);

public record MembershipRecord(
    Guid Id,
    Guid OrgId,
    Guid UserId,
    string Role,
    string Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset CreatedAt
);

public record MemberRecord(
    Guid Id,
    Guid OrgId,
    Guid UserId,
    string Email,
    string? FullName,
    string Role,
    string Status,
    DateTimeOffset JoinedAt
);

public record InviteRecord(
    Guid Id,
    Guid OrgId,
    string Email,
    string EmailNormalized,
    string Role,
    Guid InvitedByUserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset CreatedAt
);

