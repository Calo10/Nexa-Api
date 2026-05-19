using NexaApi.Data;
using NexaApi.DTOs.Members;

namespace NexaApi.Services;

public class MembersService : IMembersService
{
    private readonly IOrgRepository _orgRepository;
    private readonly ILogger<MembersService> _logger;

    public MembersService(IOrgRepository orgRepository, ILogger<MembersService> logger)
    {
        _orgRepository = orgRepository;
        _logger = logger;
    }

    public async Task<List<MemberResponse>> ListMembersAsync(Guid orgId, Guid requesterUserId, CancellationToken cancellationToken = default)
    {
        // Validate requester has membership
        var requesterMembership = await _orgRepository.GetMembershipAsync(orgId, requesterUserId, cancellationToken);
        if (requesterMembership == null)
        {
            _logger.LogWarning("User {UserId} attempted to list members of org {OrgId} without membership", requesterUserId, orgId);
            return new List<MemberResponse>();
        }

        // List members
        var members = await _orgRepository.ListMembersAsync(orgId, cancellationToken);

        return members.Select(m => new MemberResponse
        {
            MemberId = m.Id,
            UserId = m.UserId,
            Email = m.Email,
            FullName = m.FullName,
            Role = m.Role,
            JoinedAt = m.JoinedAt
        }).ToList();
    }

    public async Task<bool> ChangeRoleAsync(Guid orgId, Guid requesterUserId, Guid memberId, string newRole, CancellationToken cancellationToken = default)
    {
        // Validate requester is owner/admin
        var requesterMembership = await _orgRepository.GetMembershipAsync(orgId, requesterUserId, cancellationToken);
        if (requesterMembership == null)
        {
            _logger.LogWarning("User {UserId} attempted to change role in org {OrgId} without membership", requesterUserId, orgId);
            return false;
        }

        if (requesterMembership.Role != "owner" && requesterMembership.Role != "admin")
        {
            _logger.LogWarning("User {UserId} attempted to change role in org {OrgId} without owner/admin role", requesterUserId, orgId);
            return false;
        }

        // Get target member
        var members = await _orgRepository.ListMembersAsync(orgId, cancellationToken);
        var targetMember = members.FirstOrDefault(m => m.Id == memberId);
        if (targetMember == null)
        {
            _logger.LogWarning("Member {MemberId} not found in org {OrgId}", memberId, orgId);
            return false;
        }

        // Prevent removing last owner
        if (targetMember.Role == "owner" && newRole != "owner")
        {
            var ownerCount = await _orgRepository.CountOwnersAsync(orgId, cancellationToken);
            if (ownerCount <= 1)
            {
                _logger.LogWarning("Attempted to change role of last owner {MemberId} in org {OrgId}", memberId, orgId);
                return false;
            }
        }

        // Change role
        await _orgRepository.ChangeMemberRoleAsync(orgId, memberId, newRole, cancellationToken: cancellationToken);

        _logger.LogInformation("Role changed for member {MemberId} in org {OrgId} to {NewRole}", memberId, orgId, newRole);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid orgId, Guid requesterUserId, Guid memberId, CancellationToken cancellationToken = default)
    {
        // Validate requester is owner/admin
        var requesterMembership = await _orgRepository.GetMembershipAsync(orgId, requesterUserId, cancellationToken);
        if (requesterMembership == null)
        {
            _logger.LogWarning("User {UserId} attempted to remove member from org {OrgId} without membership", requesterUserId, orgId);
            return false;
        }

        if (requesterMembership.Role != "owner" && requesterMembership.Role != "admin")
        {
            _logger.LogWarning("User {UserId} attempted to remove member from org {OrgId} without owner/admin role", requesterUserId, orgId);
            return false;
        }

        // Get target member
        var members = await _orgRepository.ListMembersAsync(orgId, cancellationToken);
        var targetMember = members.FirstOrDefault(m => m.Id == memberId);
        if (targetMember == null)
        {
            _logger.LogWarning("Member {MemberId} not found in org {OrgId}", memberId, orgId);
            return false;
        }

        // Prevent removing last owner
        if (targetMember.Role == "owner")
        {
            var ownerCount = await _orgRepository.CountOwnersAsync(orgId, cancellationToken);
            if (ownerCount <= 1)
            {
                _logger.LogWarning("Attempted to remove last owner {MemberId} from org {OrgId}", memberId, orgId);
                return false;
            }
        }

        // Remove member
        await _orgRepository.RemoveMemberAsync(orgId, memberId, cancellationToken: cancellationToken);

        _logger.LogInformation("Member {MemberId} removed from org {OrgId}", memberId, orgId);
        return true;
    }
}
