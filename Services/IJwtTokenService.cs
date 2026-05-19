namespace NexaApi.Services;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, Guid? orgId = null, Guid? membershipId = null, string? role = null, string? featuresJson = null);
}

