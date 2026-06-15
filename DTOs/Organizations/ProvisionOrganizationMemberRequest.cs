namespace NexaApi.DTOs.Organizations;

public class ProvisionOrganizationMemberRequest
{
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "admin";
}
