using NexaApi.DTOs.Features;

namespace NexaApi.Services;

public interface IFeaturesService
{
    Task<Dictionary<string, object>> GetResolvedFeaturesAsync(Guid orgId, CancellationToken cancellationToken = default);
    bool IsEnabled(Dictionary<string, object> features, string key);
    int GetLimit(Dictionary<string, object> features, string key);
}

