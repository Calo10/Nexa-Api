using NexaApi.DTOs.Members;

namespace NexaApi.Services;

public interface IMembersService
{
    Task<List<MemberResponse>> ListMembersAsync(Guid orgId, Guid requesterUserId, CancellationToken cancellationToken = default);
    Task<bool> ChangeRoleAsync(Guid orgId, Guid requesterUserId, Guid memberId, string newRole, CancellationToken cancellationToken = default);
    Task<bool> RemoveMemberAsync(Guid orgId, Guid requesterUserId, Guid memberId, CancellationToken cancellationToken = default);
}

