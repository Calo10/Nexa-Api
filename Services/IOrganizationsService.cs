using NexaApi.DTOs.Organizations;

namespace NexaApi.Services;

public interface IOrganizationsService
{
    Task<ProvisionOrganizationResponse?> ProvisionOrganizationAsync(
        string name,
        string timezone,
        string adminEmail,
        string? adminFullName,
        string? adminPassword = null,
        CancellationToken cancellationToken = default);

    Task<ProvisionOrganizationResponse?> ProvisionOrganizationMemberAsync(
        Guid orgId,
        string email,
        string? fullName,
        string password,
        string role = "admin",
        CancellationToken cancellationToken = default);

    Task<OrganizationResponse?> CreateOrganizationAsync(string name, string timezone, Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<List<OrganizationResponse>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<OrganizationDetailResponse?> GetOrganizationAsync(Guid orgId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> SwitchActiveOrganizationAsync(Guid orgId, Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}

