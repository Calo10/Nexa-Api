using Dapper;
using System.Data;

namespace NexaApi.Data;

public class BillingRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BillingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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

    public async Task<SubscriptionRecord?> GetSubscriptionByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default)
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

    public async Task<SubscriptionWithPlanRecord?> GetSubscriptionWithPlanByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                s.id AS Id,
                s.org_id AS OrgId,
                s.plan_id AS PlanId,
                s.status AS Status,
                s.stripe_customer_id AS StripeCustomerId,
                s.stripe_subscription_id AS StripeSubscriptionId,
                s.current_period_start AS CurrentPeriodStart,
                s.current_period_end AS CurrentPeriodEnd,
                s.cancel_at_period_end AS CancelAtPeriodEnd,
                s.canceled_at AS CanceledAt,
                s.created_at AS CreatedAt,
                s.updated_at AS UpdatedAt,
                p.key AS PlanKey,
                p.name AS PlanName,
                p.price_cents AS PriceCents
            FROM subscriptions s
            INNER JOIN plans p ON s.plan_id = p.id
            WHERE s.org_id = @OrgId";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<SubscriptionWithPlanRecord>(
            new CommandDefinition(sql, new { OrgId = orgId }, cancellationToken: cancellationToken));

        return result;
    }

    public async Task UpsertSubscriptionAsync(
        Guid orgId,
        Guid planId,
        string status,
        string? stripeCustomerId,
        string? stripeSubscriptionId,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        bool cancelAtPeriodEnd,
        DateTimeOffset? canceledAt,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            MERGE subscriptions AS target
            USING (SELECT @OrgId AS org_id) AS source
            ON target.org_id = source.org_id
            WHEN MATCHED THEN
                UPDATE SET
                    plan_id = @PlanId,
                    status = @Status,
                    stripe_customer_id = ISNULL(@StripeCustomerId, target.stripe_customer_id),
                    stripe_subscription_id = ISNULL(@StripeSubscriptionId, target.stripe_subscription_id),
                    current_period_start = @CurrentPeriodStart,
                    current_period_end = @CurrentPeriodEnd,
                    cancel_at_period_end = @CancelAtPeriodEnd,
                    canceled_at = @CanceledAt,
                    updated_at = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT (id, org_id, plan_id, status, stripe_customer_id, stripe_subscription_id, 
                        current_period_start, current_period_end, cancel_at_period_end, canceled_at, 
                        created_at, updated_at)
                VALUES (@Id, @OrgId, @PlanId, @Status, @StripeCustomerId, @StripeSubscriptionId,
                        @CurrentPeriodStart, @CurrentPeriodEnd, @CancelAtPeriodEnd, @CanceledAt,
                        @CreatedAt, @UpdatedAt);";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        var now = Sql.UtcNow;
        var id = Guid.NewGuid();

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    Id = id,
                    OrgId = orgId,
                    PlanId = planId,
                    Status = status,
                    StripeCustomerId = stripeCustomerId,
                    StripeSubscriptionId = stripeSubscriptionId,
                    CurrentPeriodStart = currentPeriodStart,
                    CurrentPeriodEnd = currentPeriodEnd,
                    CancelAtPeriodEnd = cancelAtPeriodEnd,
                    CanceledAt = canceledAt,
                    CreatedAt = now,
                    UpdatedAt = now
                }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task UpdateSubscriptionStatusAsync(
        Guid orgId,
        string status,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset? canceledAt,
        bool? cancelAtPeriodEnd,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<string> { "status = @Status", "updated_at = @UpdatedAt" };
        var parameters = new Dictionary<string, object?> { { "OrgId", orgId }, { "Status", status }, { "UpdatedAt", Sql.UtcNow } };

        if (currentPeriodStart.HasValue)
        {
            updates.Add("current_period_start = @CurrentPeriodStart");
            parameters["CurrentPeriodStart"] = currentPeriodStart.Value;
        }

        if (currentPeriodEnd.HasValue)
        {
            updates.Add("current_period_end = @CurrentPeriodEnd");
            parameters["CurrentPeriodEnd"] = currentPeriodEnd.Value;
        }

        if (canceledAt.HasValue)
        {
            updates.Add("canceled_at = @CanceledAt");
            parameters["CanceledAt"] = canceledAt.Value;
        }
        else if (canceledAt == null && status != "canceled")
        {
            updates.Add("canceled_at = NULL");
        }

        if (cancelAtPeriodEnd.HasValue)
        {
            updates.Add("cancel_at_period_end = @CancelAtPeriodEnd");
            parameters["CancelAtPeriodEnd"] = cancelAtPeriodEnd.Value ? 1 : 0;
        }

        var sql = $@"
            UPDATE subscriptions
            SET {string.Join(", ", updates)}
            WHERE org_id = @OrgId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task UpdateCancelAtPeriodEndAsync(
        Guid orgId,
        bool cancelAtPeriodEnd,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE subscriptions
            SET cancel_at_period_end = @CancelAtPeriodEnd,
                updated_at = @UpdatedAt
            WHERE org_id = @OrgId";

        var connection = transaction?.Connection ?? _connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open && transaction == null)
        {
            connection.Open();
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new
                {
                    OrgId = orgId,
                    CancelAtPeriodEnd = cancelAtPeriodEnd ? 1 : 0,
                    UpdatedAt = Sql.UtcNow
                }, transaction, cancellationToken: cancellationToken));
        }
        finally
        {
            if (transaction == null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    public async Task<SubscriptionRecord?> GetSubscriptionByStripeSubscriptionIdAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
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
            WHERE stripe_subscription_id = @StripeSubscriptionId";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var result = await connection.QueryFirstOrDefaultAsync<SubscriptionRecord>(
            new CommandDefinition(sql, new { StripeSubscriptionId = stripeSubscriptionId }, cancellationToken: cancellationToken));

        return result;
    }
}

public record SubscriptionWithPlanRecord(
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
    DateTimeOffset UpdatedAt,
    string PlanKey,
    string PlanName,
    int PriceCents
);

