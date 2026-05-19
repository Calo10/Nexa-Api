using Dapper;
using System.Data;

namespace NexaApi.Data;

public class FeaturesRepository : IFeaturesRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public FeaturesRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SubscriptionRecord?> GetSubscriptionForOrgAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                org_id AS OrgId,
                plan_id AS PlanId,
                status AS Status,
                stripe_customer_id AS StripeCustomerId,
                stripe_subscription_id AS StripeSubscriptionId,
                current_period_start AS CurrentPeriodStart,
                current_period_end AS CurrentPeriodEnd,
                cancel_at_period_end AS CancelAtPeriodEnd,
                canceled_at AS CanceledAt,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM subscriptions
            WHERE org_id = @OrgId";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<SubscriptionRecord>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task<PlanRecord?> GetPlanByKeyAsync(string planKey, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                [key] AS [Key],
                name AS Name,
                interval AS Interval,
                currency AS Currency,
                price_cents AS PriceCents,
                trial_days AS TrialDays,
                is_active AS IsActive,
                created_at AS CreatedAt
            FROM plans
            WHERE [key] = @PlanKey
              AND is_active = 1";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<PlanRecord>(
            new CommandDefinition(sql, new { PlanKey = planKey }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task<List<PlanFeatureRecord>> GetPlanFeaturesAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                plan_id AS PlanId,
                feature_key AS FeatureKey,
                value_json AS ValueJson
            FROM plan_features
            WHERE plan_id = @PlanId";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = await connection.QueryAsync<PlanFeatureRecord>(
            new CommandDefinition(sql, new { PlanId = planId }, cancellationToken: cancellationToken));

        return results.ToList();
    }

    public async Task<List<OrgFeatureOverrideRecord>> GetOrgFeatureOverridesAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                org_id AS OrgId,
                feature_key AS FeatureKey,
                value_json AS ValueJson,
                created_at AS CreatedAt
            FROM org_feature_overrides
            WHERE org_id = @OrgId";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = await connection.QueryAsync<OrgFeatureOverrideRecord>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return results.ToList();
    }
}

