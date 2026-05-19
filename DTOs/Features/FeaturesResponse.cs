namespace NexaApi.DTOs.Features;

public class FeaturesResponse
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
}

