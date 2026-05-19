namespace NexaApi.DTOs.Auth;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool RequiresOrgSetup { get; set; }
    public UserInfo? User { get; set; }
    public OrganizationInfo? Organization { get; set; }
    public Dictionary<string, object>? Features { get; set; }
}

public class UserInfo
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

public class OrganizationInfo
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
}

