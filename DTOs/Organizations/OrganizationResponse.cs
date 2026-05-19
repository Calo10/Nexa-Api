namespace NexaApi.DTOs.Organizations;

public class OrganizationResponse
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
}

