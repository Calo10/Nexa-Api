using NexaApi.DTOs.Organizations;

namespace NexaApi.Services;

public interface IOrganizationsService
{
    Task<OrganizationResponse?> CreateOrganizationAsync(string name, string timezone, Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<List<OrganizationResponse>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<OrganizationDetailResponse?> GetOrganizationAsync(Guid orgId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> SwitchActiveOrganizationAsync(Guid orgId, Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}

