namespace NexaApi.DTOs.Organizations;

public class ProvisionOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public string AdminEmail { get; set; } = string.Empty;
    public string? AdminFullName { get; set; }
}
