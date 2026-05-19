using NexaApi.DTOs.Invites;

namespace NexaApi.Services;

public interface IInvitesService
{
    Task<InviteResponse?> SendInviteAsync(Guid orgId, Guid inviterUserId, string email, string role, CancellationToken cancellationToken = default);
    Task<bool> AcceptInviteAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> ResendInviteAsync(Guid orgId, Guid userId, Guid inviteId, CancellationToken cancellationToken = default);
}

