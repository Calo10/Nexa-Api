namespace NexaApi.DTOs.Organizations;

public class CreateOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
}

