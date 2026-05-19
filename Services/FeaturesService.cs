using System.Text.Json;
using NexaApi.Data;
using NexaApi.DTOs.Features;

namespace NexaApi.Services;

public class FeaturesService : IFeaturesService
{
    private readonly IFeaturesRepository _featuresRepository;
    private readonly ILogger<FeaturesService> _logger;

    public FeaturesService(IFeaturesRepository featuresRepository, ILogger<FeaturesService> logger)
    {
        _featuresRepository = featuresRepository;
        _logger = logger;
    }

    public async Task<Dictionary<string, object>> GetResolvedFeaturesAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        // 1. Load subscription for org
        var subscription = await _featuresRepository.GetSubscriptionForOrgAsync(orgId, cancellationToken);

        Guid planId;
        string status;

        if (subscription == null)
        {
            // If missing, treat as 'starter' plan and status 'active'
            var starterPlan = await _featuresRepository.GetPlanByKeyAsync("starter", cancellationToken);
            if (starterPlan == null)
            {
                _logger.LogWarning("Starter plan not found, returning empty features");
                return new Dictionary<string, object>();
            }
            planId = starterPlan.Id;
            status = "active";
        }
        else
        {
            // 2. If subscription exists, use its plan_id and status
            planId = subscription.PlanId;
            status = subscription.Status;
        }

        // 3. Load plan_features for the plan
        var planFeatures = await _featuresRepository.GetPlanFeaturesAsync(planId, cancellationToken);

        // 4. Load org_feature_overrides
        var orgOverrides = await _featuresRepository.GetOrgFeatureOverridesAsync(orgId, cancellationToken);

        // 5. Merge by feature_key (org override wins)
        var features = new Dictionary<string, object>();

        // Start with plan features
        foreach (var planFeature in planFeatures)
        {
            var value = ParseJsonValue(planFeature.ValueJson);
            features[planFeature.FeatureKey] = value;
        }

        // Apply org overrides (they win)
        foreach (var orgOverride in orgOverrides)
        {
            var value = ParseJsonValue(orgOverride.ValueJson);
            features[orgOverride.FeatureKey] = value;
        }

        return features;
    }

    public bool IsEnabled(Dictionary<string, object> features, string key)
    {
        if (!features.TryGetValue(key, out var value))
        {
            return false;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.GetBoolean();
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }

    public int GetLimit(Dictionary<string, object> features, string key)
    {
        if (!features.TryGetValue(key, out var value))
        {
            return 0;
        }

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.GetInt32();
            }
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return (int)longValue;
        }

        return 0;
    }

    private object ParseJsonValue(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonElement();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON value: {Json}", json);
            return new JsonElement();
        }
    }
}

