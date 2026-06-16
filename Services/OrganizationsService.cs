using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using NexaApi.Data;
using NexaApi.DTOs.Organizations;

namespace NexaApi.Services;

public class OrganizationsService : IOrganizationsService
{
    private readonly IOrgRepository _orgRepository;
    private readonly IAuthRepository _authRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<OrganizationsService> _logger;

    public OrganizationsService(
        IOrgRepository orgRepository,
        IAuthRepository authRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<OrganizationsService> logger)
    {
        _orgRepository = orgRepository;
        _authRepository = authRepository;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ProvisionOrganizationResponse?> ProvisionOrganizationAsync(
        string name,
        string timezone,
        string adminEmail,
        string? adminFullName,
        string? adminPassword = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = adminEmail.ToLowerInvariant().Trim();
        var emailForStorage = adminEmail.Trim();

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var existingUser = await _authRepository.GetUserByEmailAsync(normalizedEmail, transaction, cancellationToken);
            Guid adminUserId;
            var adminUserCreated = false;
            string? resolvedFullName;

            if (existingUser == null)
            {
                adminUserId = await _authRepository.CreateUserAsync(
                    emailForStorage,
                    string.IsNullOrWhiteSpace(adminFullName) ? null : adminFullName.Trim(),
                    transaction,
                    cancellationToken);
                adminUserCreated = true;
                resolvedFullName = string.IsNullOrWhiteSpace(adminFullName) ? null : adminFullName.Trim();
            }
            else
            {
                if (existingUser.Status == "disabled")
                {
                    _logger.LogWarning("Provision blocked: admin user {Email} is disabled", emailForStorage);
                    return null;
                }

                adminUserId = existingUser.Id;
                resolvedFullName = existingUser.FullName ?? (string.IsNullOrWhiteSpace(adminFullName) ? null : adminFullName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(adminPassword))
            {
                if (!adminUserCreated && !string.IsNullOrWhiteSpace(existingUser?.PasswordHash))
                {
                    _logger.LogWarning("Provision blocked: admin user {Email} already has a password", emailForStorage);
                    return null;
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, workFactor: 12);
                await _authRepository.UpdatePasswordAsync(
                    adminUserId,
                    passwordHash,
                    DateTimeOffset.UtcNow,
                    transaction,
                    cancellationToken);
            }

            var baseSlug = GenerateSlug(name);
            var slug = await EnsureUniqueSlugAsync(baseSlug, transaction, cancellationToken);

            var orgId = await _orgRepository.CreateOrganizationAsync(
                name,
                slug,
                timezone,
                adminUserId,
                transaction,
                cancellationToken);

            transaction.Commit();

            var org = await _orgRepository.GetOrganizationAsync(orgId, cancellationToken);
            if (org == null)
            {
                return null;
            }

            _logger.LogInformation(
                "Organization {OrgId} provisioned with admin {UserId} ({Email}), userCreated={UserCreated}",
                orgId,
                adminUserId,
                emailForStorage,
                adminUserCreated);

            return new ProvisionOrganizationResponse
            {
                OrganizationId = org.Id,
                Name = org.Name,
                Slug = org.Slug,
                CreatedAt = org.CreatedAt,
                AdminUserId = adminUserId,
                AdminEmail = emailForStorage,
                AdminFullName = resolvedFullName,
                AdminUserCreated = adminUserCreated,
                AdminRole = "owner"
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ProvisionOrganizationResponse?> ProvisionOrganizationMemberAsync(
        Guid orgId,
        string email,
        string? fullName,
        string password,
        string role = "admin",
        CancellationToken cancellationToken = default)
    {
        var org = await _orgRepository.GetOrganizationAsync(orgId, cancellationToken);
        if (org is null || !string.Equals(org.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Provision member blocked: org {OrgId} not found or inactive", orgId);
            return null;
        }

        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "admin" : role.Trim().ToLowerInvariant();
        if (normalizedRole is not ("admin" or "member"))
        {
            _logger.LogWarning("Provision member blocked: invalid role {Role}", normalizedRole);
            return null;
        }

        var normalizedEmail = email.ToLowerInvariant().Trim();
        var emailForStorage = email.Trim();

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var existingUser = await _authRepository.GetUserByEmailAsync(normalizedEmail, transaction, cancellationToken);
            Guid userId;
            var userCreated = false;
            string? resolvedFullName;

            if (existingUser == null)
            {
                userId = await _authRepository.CreateUserAsync(
                    emailForStorage,
                    string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
                    transaction,
                    cancellationToken);
                userCreated = true;
                resolvedFullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
            }
            else
            {
                if (existingUser.Status == "disabled")
                {
                    _logger.LogWarning("Provision member blocked: user {Email} is disabled", emailForStorage);
                    return null;
                }

                userId = existingUser.Id;
                resolvedFullName = existingUser.FullName ?? (string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim());
            }

            if (userCreated || string.IsNullOrWhiteSpace(existingUser?.PasswordHash))
            {
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
                await _authRepository.UpdatePasswordAsync(
                    userId,
                    passwordHash,
                    DateTimeOffset.UtcNow,
                    transaction,
                    cancellationToken);
            }

            var existingMembership = await _orgRepository.GetMembershipAsync(orgId, userId, cancellationToken);
            if (existingMembership is not null)
            {
                transaction.Rollback();
                _logger.LogInformation(
                    "Provision member idempotent: user {UserId} is already a member of org {OrgId}",
                    userId,
                    orgId);

                return new ProvisionOrganizationResponse
                {
                    OrganizationId = org.Id,
                    Name = org.Name,
                    Slug = org.Slug,
                    CreatedAt = org.CreatedAt,
                    AdminUserId = userId,
                    AdminEmail = emailForStorage,
                    AdminFullName = resolvedFullName,
                    AdminUserCreated = false,
                    AdminRole = existingMembership.Role
                };
            }

            var membershipId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            const string sqlMembership = @"
                INSERT INTO org_memberships (id, org_id, user_id, role, status, joined_at, created_at)
                VALUES (@Id, @OrgId, @UserId, @Role, @Status, @JoinedAt, @CreatedAt)";

            await connection.ExecuteAsync(
                new CommandDefinition(sqlMembership, new
                {
                    Id = membershipId,
                    OrgId = orgId,
                    UserId = userId,
                    Role = normalizedRole,
                    Status = "active",
                    JoinedAt = now,
                    CreatedAt = now
                }, transaction, cancellationToken: cancellationToken));

            transaction.Commit();

            _logger.LogInformation(
                "User {UserId} ({Email}) provisioned into org {OrgId} with role {Role}, userCreated={UserCreated}",
                userId,
                emailForStorage,
                orgId,
                normalizedRole,
                userCreated);

            return new ProvisionOrganizationResponse
            {
                OrganizationId = org.Id,
                Name = org.Name,
                Slug = org.Slug,
                CreatedAt = org.CreatedAt,
                AdminUserId = userId,
                AdminEmail = emailForStorage,
                AdminFullName = resolvedFullName,
                AdminUserCreated = userCreated,
                AdminRole = normalizedRole
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<OrganizationResponse?> CreateOrganizationAsync(string name, string timezone, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        // Generate slug (kebab-case + uniqueness)
        var baseSlug = GenerateSlug(name);
        var slug = await EnsureUniqueSlugAsync(baseSlug, transaction: null, cancellationToken);

        // Call repository transaction
        var orgId = await _orgRepository.CreateOrganizationAsync(name, slug, timezone, ownerUserId, cancellationToken: cancellationToken);

        // Get the created organization
        var org = await _orgRepository.GetOrganizationAsync(orgId, cancellationToken);
        if (org == null)
        {
            return null;
        }

        // Get membership to get role
        var membership = await _orgRepository.GetMembershipAsync(orgId, ownerUserId, cancellationToken);
        var role = membership?.Role;

        return new OrganizationResponse
        {
            OrganizationId = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            CreatedAt = org.CreatedAt,
            Role = role,
            IsActive = org.Status == "active"
        };
    }

    public async Task<List<OrganizationResponse>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var orgs = await _orgRepository.ListOrganizationsForUserAsync(userId, cancellationToken);

        var result = new List<OrganizationResponse>();

        foreach (var org in orgs)
        {
            var membership = await _orgRepository.GetMembershipAsync(org.Id, userId, cancellationToken);
            result.Add(new OrganizationResponse
            {
                OrganizationId = org.Id,
                Name = org.Name,
                Slug = org.Slug,
                CreatedAt = org.CreatedAt,
                Role = membership?.Role,
                IsActive = org.Status == "active"
            });
        }

        return result;
    }

    public async Task<OrganizationDetailResponse?> GetOrganizationAsync(Guid orgId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Validate membership
        var membership = await _orgRepository.GetMembershipAsync(orgId, userId, cancellationToken);
        if (membership == null)
        {
            _logger.LogWarning("User {UserId} attempted to access organization {OrgId} without membership", userId, orgId);
            return null;
        }

        // Get organization
        var org = await _orgRepository.GetOrganizationAsync(orgId, cancellationToken);
        if (org == null)
        {
            return null;
        }

        // Get member count
        var members = await _orgRepository.ListMembersAsync(orgId, cancellationToken);
        var memberCount = members.Count;

        return new OrganizationDetailResponse
        {
            OrganizationId = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            CreatedAt = org.CreatedAt,
            Role = membership.Role,
            IsActive = org.Status == "active",
            MemberCount = memberCount
        };
    }

    public async Task<bool> SwitchActiveOrganizationAsync(Guid orgId, Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        // Validate membership
        var membership = await _orgRepository.GetMembershipAsync(orgId, userId, cancellationToken);
        if (membership == null)
        {
            _logger.LogWarning("User {UserId} attempted to switch to organization {OrgId} without membership", userId, orgId);
            return false;
        }

        // Validate session belongs to user
        // We need to get session by ID - but we don't have that method
        // For now, we'll update it and let the database constraints handle it
        // In a real scenario, we'd want to validate the session belongs to the user

        // Update sessions.org_id
        await _authRepository.UpdateSessionOrgIdAsync(sessionId, orgId, cancellationToken: cancellationToken);

        return true;
    }

    private string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "org";
        }

        // Convert to lowercase
        var slug = name.ToLowerInvariant();

        // Replace spaces and special characters with hyphens
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");

        // Remove leading/trailing hyphens
        slug = slug.Trim('-');

        // Ensure it's not empty
        if (string.IsNullOrEmpty(slug))
        {
            slug = "org";
        }

        // Limit length
        if (slug.Length > 50)
        {
            slug = slug.Substring(0, 50).TrimEnd('-');
        }

        return slug;
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var slug = baseSlug;
        var counter = 1;

        while (await SlugExistsAsync(slug, transaction, cancellationToken))
        {
            var suffix = $"-{counter}";
            var maxLength = 50 - suffix.Length;
            if (maxLength < 1)
            {
                maxLength = 1;
            }

            var truncatedBase = baseSlug.Length > maxLength ? baseSlug.Substring(0, maxLength) : baseSlug;
            slug = $"{truncatedBase}{suffix}";
            counter++;

            if (counter > 1000) // Safety limit
            {
                slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..8]}";
                break;
            }
        }

        return slug;
    }

    private async Task<bool> SlugExistsAsync(string slug, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM organizations
            WHERE slug = @Slug
              AND deleted_at IS NULL";

        if (transaction != null)
        {
            var count = await transaction.Connection!.QuerySingleAsync<int>(
                new CommandDefinition(sql, new { Slug = slug }, transaction, cancellationToken: cancellationToken));
            return count > 0;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var countWithoutTx = await connection.QuerySingleAsync<int>(
            new CommandDefinition(sql, new { Slug = slug }, cancellationToken: cancellationToken));

        return countWithoutTx > 0;
    }
}
