namespace NexaApi.DTOs.Auth;

public class UserInfoResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public ActiveOrganization? ActiveOrganization { get; set; }
    public string? Role { get; set; }
    public List<string> Features { get; set; } = new();
}

public class ActiveOrganization
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
}

