namespace NexaApi.DTOs.Organizations;

public class OrganizationDetailResponse
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
}

