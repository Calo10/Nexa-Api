namespace NexaApi.DTOs.Organizations;

public class ProvisionOrganizationResponse
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid AdminUserId { get; set; }
    public string AdminEmail { get; set; } = string.Empty;
    public string? AdminFullName { get; set; }
    public bool AdminUserCreated { get; set; }
    public string AdminRole { get; set; } = "owner";
}
