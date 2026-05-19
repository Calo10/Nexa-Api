namespace NexaApi.Data;

public interface IFeaturesRepository
{
    Task<SubscriptionRecord?> GetSubscriptionForOrgAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task<PlanRecord?> GetPlanByKeyAsync(string planKey, CancellationToken cancellationToken = default);
    Task<List<PlanFeatureRecord>> GetPlanFeaturesAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<List<OrgFeatureOverrideRecord>> GetOrgFeatureOverridesAsync(Guid orgId, CancellationToken cancellationToken = default);
}

public record PlanRecord(
    Guid Id,
    string Key,
    string Name,
    string Interval,
    string Currency,
    int PriceCents,
    int TrialDays,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record SubscriptionRecord(
    Guid Id,
    Guid OrgId,
    Guid PlanId,
    string Status,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record PlanFeatureRecord(
    Guid PlanId,
    string FeatureKey,
    string ValueJson
);

public record OrgFeatureOverrideRecord(
    Guid OrgId,
    string FeatureKey,
    string ValueJson,
    DateTimeOffset CreatedAt
);

