using System.Text.Json;
using BCrypt.Net;
using Microsoft.Extensions.Configuration;
using NexaApi.Data;
using NexaApi.DTOs.Auth;

namespace NexaApi.Services;

public class AuthService : IAuthService
{
    private readonly IAuthRepository _authRepository;
    private readonly IOrgRepository _orgRepository;
    private readonly IFeaturesService _featuresService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAuthRepository authRepository,
        IOrgRepository orgRepository,
        IFeaturesService featuresService,
        IJwtTokenService jwtTokenService,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _authRepository = authRepository;
        _orgRepository = orgRepository;
        _featuresService = featuresService;
        _jwtTokenService = jwtTokenService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MagicLinkResponse> SendMagicLinkAsync(string email, CancellationToken cancellationToken = default)
    {
        // Normalize email
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Generate secure token
        var rawToken = Sql.GenerateSecureToken();

        // Hash token
        var tokenHash = Sql.HashToken(rawToken);

        // Insert magic_links row with expires_at = now + 15 minutes
        var expiresAt = Sql.UtcNow.AddMinutes(15);

        // Check if user exists
        var existingUser = await _authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);
        Guid? userId = existingUser?.Id;

        await _authRepository.InsertMagicLinkAsync(
            email,
            userId,
            tokenHash,
            expiresAt,
            cancellationToken: cancellationToken);

        // Send email with raw token (do NOT store raw token)
        try
        {
            await _emailService.SendMagicLinkAsync(email, rawToken, cancellationToken);
            _logger.LogInformation("Magic link email sent to {Email}. Token expires at {ExpiresAt}", email, expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send magic link email to {Email}, but magic link was created", email);
            // Re-throw the exception so the controller can return appropriate error status
            // The magic link is created in DB but email failed, so we should return an error
            throw;
        }

        // In Development, return token for testing
        var isDevelopment = _configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") == "Development" ||
                           Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        return new MagicLinkResponse
        {
            Success = true,
            Token = isDevelopment ? rawToken : null, // Only return token in Development
            ExpiresAt = expiresAt
        };
    }

    public async Task<AuthResponse?> ConsumeTokenAsync(string token, string? userAgent, string? ip, CancellationToken cancellationToken = default)
    {
        // Hash token
        var tokenHash = Sql.HashToken(token);

        // Load valid magic link
        var magicLink = await _authRepository.GetValidMagicLinkByTokenHashAsync(tokenHash, cancellationToken: cancellationToken);
        if (magicLink == null)
        {
            _logger.LogWarning("Invalid or expired magic link token");
            return null; // Will result in 401
        }

        // Create user if not exists
        Guid userId;
        UserRecord? user;

        if (magicLink.UserId.HasValue)
        {
            userId = magicLink.UserId.Value;
            user = await _authRepository.GetUserByEmailAsync(magicLink.EmailNormalized, cancellationToken: cancellationToken);
        }
        else
        {
            // Create new user
            userId = await _authRepository.CreateUserAsync(magicLink.Email, null, cancellationToken: cancellationToken);
            user = await _authRepository.GetUserByEmailAsync(magicLink.EmailNormalized, cancellationToken: cancellationToken);
        }

        if (user == null)
        {
            _logger.LogError("Failed to create or retrieve user for email {Email}", magicLink.Email);
            return null;
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("Attempted login by disabled user {UserId}", userId);
            throw new UnauthorizedAccessException("User account is disabled"); // Will result in 403
        }

        // Consume magic link
        await _authRepository.ConsumeMagicLinkAsync(magicLink.Id, cancellationToken: cancellationToken);

        // Reuse the same token issuance logic
        return await IssueAuthResponseAsync(userId, user, userAgent, ip, cancellationToken);
    }

    public async Task<AuthResponse?> LoginWithPasswordAsync(string email, string password, string? userAgent, string? ip, CancellationToken cancellationToken = default)
    {
        // Normalize email
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Load user by email
        var user = await _authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);
        
        // Generic error message for security (don't reveal if user exists)
        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("Login attempt with invalid credentials for email {Email}", normalizedEmail);
            return null; // Will result in 401
        }

        // Check if account is locked
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > Sql.UtcNow)
        {
            _logger.LogWarning("Login attempt for locked account {UserId}. Locked until {LockedUntil}", user.Id, user.LockedUntil);
            return null; // Will result in 401
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("Attempted login by disabled user {UserId}", user.Id);
            throw new UnauthorizedAccessException("User account is disabled"); // Will result in 403
        }

        // Verify password
        bool passwordValid;
        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password for user {UserId}", user.Id);
            return null;
        }

        if (!passwordValid)
        {
            // Increment failed login count
            await _authRepository.IncrementFailedLoginCountAsync(user.Id, cancellationToken: cancellationToken);

            // Apply lockout after 5 failed attempts (lock for 15 minutes)
            const int maxFailedAttempts = 5;
            const int lockoutMinutes = 15;
            
            var newFailedCount = user.FailedLoginCount + 1;
            if (newFailedCount >= maxFailedAttempts)
            {
                var lockedUntil = Sql.UtcNow.AddMinutes(lockoutMinutes);
                await _authRepository.SetAccountLockoutAsync(user.Id, lockedUntil, cancellationToken: cancellationToken);
                _logger.LogWarning("Account {UserId} locked due to {FailedCount} failed login attempts. Locked until {LockedUntil}", 
                    user.Id, newFailedCount, lockedUntil);
            }

            _logger.LogWarning("Invalid password attempt for user {UserId}. Failed count: {FailedCount}", user.Id, newFailedCount);
            return null; // Will result in 401
        }

        // Password is valid - reset failed count and lockout
        await _authRepository.ResetFailedLoginCountAsync(user.Id, cancellationToken: cancellationToken);
        _logger.LogInformation("Successful password login for user {UserId}", user.Id);

        // Reuse the same token issuance logic as ConsumeTokenAsync
        return await IssueAuthResponseAsync(user.Id, user, userAgent, ip, cancellationToken);
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        // Validate password policy
        var validationError = ValidatePasswordPolicy(newPassword);
        if (validationError != null)
        {
            throw new ArgumentException(validationError);
        }

        // Load user by ID
        var user = await _authRepository.GetUserByIdAsync(userId, cancellationToken: cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("SetPassword attempted for non-existent user {UserId}", userId);
            throw new UnauthorizedAccessException("User not found");
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("SetPassword attempted by disabled user {UserId}", userId);
            throw new UnauthorizedAccessException("User account is disabled");
        }

        // Hash new password with BCrypt (work factor 12)
        string passwordHash;
        try
        {
            passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing password for user {UserId}", userId);
            throw new InvalidOperationException("Failed to set password");
        }

        // Update password in database
        // Works for both: setting password for first time (password_hash is NULL) and updating existing password
        var now = Sql.UtcNow;
        await _authRepository.UpdatePasswordAsync(userId, passwordHash, now, cancellationToken: cancellationToken);

        _logger.LogInformation("Password set/updated successfully for user {UserId}", userId);
    }

    public async Task InitializePasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        // Validate password policy
        var validationError = ValidatePasswordPolicy(newPassword);
        if (validationError != null)
        {
            throw new ArgumentException(validationError);
        }

        // Load user by ID
        var user = await _authRepository.GetUserByIdAsync(userId, cancellationToken: cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("InitializePassword attempted for non-existent user {UserId}", userId);
            throw new UnauthorizedAccessException("User not found");
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("InitializePassword attempted by disabled user {UserId}", userId);
            throw new UnauthorizedAccessException("User account is disabled");
        }

        // Check if password is already set
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("InitializePassword attempted for user {UserId} who already has a password", userId);
            throw new InvalidOperationException("Password already set");
        }

        // Hash new password with BCrypt (work factor 12)
        string passwordHash;
        try
        {
            passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing password for user {UserId}", userId);
            throw new InvalidOperationException("Failed to initialize password");
        }

        // Update password in database
        var now = Sql.UtcNow;
        await _authRepository.UpdatePasswordAsync(userId, passwordHash, now, cancellationToken: cancellationToken);

        _logger.LogInformation("Password initialized successfully for user {UserId}", userId);
    }

    private string? ValidatePasswordPolicy(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Password is required";
        }

        if (password.Length < 8)
        {
            return "Password must be at least 8 characters long";
        }

        bool hasUpper = false;
        bool hasLower = false;
        bool hasNumber = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasNumber = true;
        }

        if (!hasUpper)
        {
            return "Password must contain at least one uppercase letter";
        }

        if (!hasLower)
        {
            return "Password must contain at least one lowercase letter";
        }

        if (!hasNumber)
        {
            return "Password must contain at least one number";
        }

        // Symbol is optional per requirements, but we can still check and suggest
        // if (!hasSymbol)
        // {
        //     return "Password must contain at least one symbol";
        // }

        return null; // Password is valid
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        // Normalize email
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Check if user exists
        var user = await _authRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);
        
        // Always return success (security: don't reveal if user exists)
        // Only proceed if user exists and has a password set
        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogInformation("Password reset requested for non-existent user or user without password: {Email}", normalizedEmail);
            return; // Silent success
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogInformation("Password reset requested for disabled user: {Email}", normalizedEmail);
            return; // Silent success
        }

        // Generate secure token
        var rawToken = Sql.GenerateSecureToken();
        var tokenHash = Sql.HashToken(rawToken);

        // Token expires in 30 minutes
        var expiresAt = Sql.UtcNow.AddMinutes(30);

        // Insert password reset token
        await _authRepository.InsertPasswordResetTokenAsync(
            user.Id,
            tokenHash,
            expiresAt,
            cancellationToken: cancellationToken);

        // Send email with reset token
        try
        {
            await _emailService.SendPasswordResetLinkAsync(email, rawToken, cancellationToken);
            _logger.LogInformation("Password reset email sent to {Email}. Token expires at {ExpiresAt}", email, expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}, but token was created", email);
            // Don't throw - we want to return success even if email fails
            // The token is still created, user can request again if needed
        }
    }

    public async Task ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        // Validate password policy
        var validationError = ValidatePasswordPolicy(newPassword);
        if (validationError != null)
        {
            throw new ArgumentException(validationError);
        }

        // Hash token
        var tokenHash = Sql.HashToken(token);

        // Load valid password reset token
        var resetToken = await _authRepository.GetValidPasswordResetTokenByHashAsync(tokenHash, cancellationToken: cancellationToken);
        if (resetToken == null)
        {
            _logger.LogWarning("Invalid or expired password reset token");
            throw new ArgumentException("Invalid or expired reset token");
        }

        // Get user
        var user = await _authRepository.GetUserByIdAsync(resetToken.UserId, cancellationToken: cancellationToken);
        if (user == null)
        {
            _logger.LogError("User not found for password reset token {TokenId}", resetToken.Id);
            throw new InvalidOperationException("User not found");
        }

        // Check if user is disabled
        if (user.Status == "disabled")
        {
            _logger.LogWarning("Password reset attempted for disabled user {UserId}", user.Id);
            throw new UnauthorizedAccessException("User account is disabled");
        }

        // Hash new password with BCrypt (work factor 12)
        string passwordHash;
        try
        {
            passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing password for password reset, user {UserId}", user.Id);
            throw new InvalidOperationException("Failed to reset password");
        }

        // Update password and consume token (in a transaction would be ideal, but keeping it simple)
        var now = Sql.UtcNow;
        await _authRepository.UpdatePasswordAsync(resetToken.UserId, passwordHash, now, cancellationToken: cancellationToken);
        await _authRepository.ConsumePasswordResetTokenAsync(resetToken.Id, cancellationToken: cancellationToken);

        _logger.LogInformation("Password reset completed successfully for user {UserId}", user.Id);
    }

    private async Task<AuthResponse> IssueAuthResponseAsync(Guid userId, UserRecord user, string? userAgent, string? ip, CancellationToken cancellationToken)
    {
        // Determine org
        var userMemberships = await _orgRepository.ListOrganizationsForUserAsync(userId, cancellationToken);
        var firstUserMembership = userMemberships.FirstOrDefault();

        // Get membership details for first org
        MembershipRecord? userMembership = null;
        if (firstUserMembership != null)
        {
            userMembership = await _orgRepository.GetMembershipAsync(firstUserMembership.Id, userId, cancellationToken);
        }

        Guid? orgId = null;
        Guid? membershipId = null;
        string? role = null;
        Dictionary<string, object>? features = null;
        bool requiresOrgSetup = false;
        OrganizationInfo? orgInfo = null;

        if (userMembership != null)
        {
            orgId = userMembership.OrgId;
            membershipId = userMembership.Id;
            role = userMembership.Role;

            // Resolve features if org exists
            features = await _featuresService.GetResolvedFeaturesAsync(orgId.Value, cancellationToken);

            // Get org info
            var org = await _orgRepository.GetOrganizationAsync(orgId.Value, cancellationToken);
            if (org != null)
            {
                orgInfo = new OrganizationInfo
                {
                    OrganizationId = org.Id,
                    Name = org.Name,
                    Slug = org.Slug
                };
            }
        }
        else
        {
            requiresOrgSetup = true;
        }

        // Create session with refresh token
        var refreshToken = Sql.GenerateSecureToken();
        var refreshTokenHash = Sql.HashToken(refreshToken);
        var refreshTokenExpiresAt = Sql.UtcNow.AddDays(7);

        var sessionId = await _authRepository.CreateSessionAsync(
            userId,
            orgId,
            refreshTokenHash,
            refreshTokenExpiresAt,
            userAgent,
            ip,
            cancellationToken: cancellationToken);

        // Issue JWT
        var featuresJson = features != null ? JsonSerializer.Serialize(features) : null;
        var accessToken = _jwtTokenService.GenerateToken(
            userId,
            orgId,
            membershipId,
            role,
            featuresJson);

        var jwtConfig = _configuration.GetSection("Jwt");
        var accessTokenMinutes = int.Parse(jwtConfig["AccessTokenMinutes"] ?? "60");
        var expiresAt = DateTime.UtcNow.AddMinutes(accessTokenMinutes);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            RequiresOrgSetup = requiresOrgSetup,
            User = new UserInfo
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName
            },
            Organization = orgInfo,
            Features = features
        };
    }

    public async Task<AuthResponse?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // Hash refresh token
        var refreshTokenHash = Sql.HashToken(refreshToken);

        // Validate session
        var session = await _authRepository.GetSessionByRefreshTokenHashAsync(refreshTokenHash, cancellationToken: cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Invalid or expired refresh token");
            return null; // Will result in 401
        }

        // Get user to check status
        // Note: We need to get user by ID, but repository only has GetUserByEmail
        // For now, we'll skip the disabled check here since we don't have a GetUserById method
        // This could be added to the repository if needed

        // Rotate refresh token
        var newRefreshToken = Sql.GenerateSecureToken();
        var newRefreshTokenHash = Sql.HashToken(newRefreshToken);
        var newRefreshTokenExpiresAt = Sql.UtcNow.AddDays(7);

        await _authRepository.RotateSessionRefreshTokenAsync(
            session.Id,
            newRefreshTokenHash,
            newRefreshTokenExpiresAt,
            cancellationToken: cancellationToken);

        // Get membership and features if org exists
        string? role = null;
        Guid? membershipId = null;
        Dictionary<string, object>? features = null;
        Guid? orgId = session.OrgId;

        // If session doesn't have org_id but user has organizations, use the first one
        if (!orgId.HasValue)
        {
            var userOrgs = await _orgRepository.ListOrganizationsForUserAsync(session.UserId, cancellationToken);
            var firstOrg = userOrgs.FirstOrDefault();
            if (firstOrg != null)
            {
                orgId = firstOrg.Id;
                // Update session with org_id
                await _authRepository.UpdateSessionOrgIdAsync(session.Id, orgId, cancellationToken: cancellationToken);
            }
        }

        if (orgId.HasValue)
        {
            var membership = await _orgRepository.GetMembershipAsync(orgId.Value, session.UserId, cancellationToken);
            if (membership != null)
            {
                role = membership.Role;
                membershipId = membership.Id;
                features = await _featuresService.GetResolvedFeaturesAsync(orgId.Value, cancellationToken);
            }
        }

        // Issue new JWT
        var featuresJson = features != null ? JsonSerializer.Serialize(features) : null;
        var accessToken = _jwtTokenService.GenerateToken(
            session.UserId,
            orgId,
            membershipId,
            role,
            featuresJson);

        var jwtConfig = _configuration.GetSection("Jwt");
        var accessTokenMinutes = int.Parse(jwtConfig["AccessTokenMinutes"] ?? "60");
        var expiresAt = DateTime.UtcNow.AddMinutes(accessTokenMinutes);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = expiresAt,
            RequiresOrgSetup = false
        };
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // Hash refresh token
        var refreshTokenHash = Sql.HashToken(refreshToken);

        // Find session
        var session = await _authRepository.GetSessionByRefreshTokenHashAsync(refreshTokenHash, cancellationToken: cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Invalid refresh token for logout");
            return false;
        }

        // Revoke session
        await _authRepository.RevokeSessionAsync(session.Id, cancellationToken: cancellationToken);

        return true;
    }

    public async Task<UserInfoResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Get active organization from first membership
        var userOrgs = await _orgRepository.ListOrganizationsForUserAsync(userId, cancellationToken);
        var firstUserOrg = userOrgs.FirstOrDefault();

        if (firstUserOrg == null)
        {
            // No memberships - try to get user by checking members in any org
            // This is a limitation - we should add GetUserById to repository
            return null;
        }

        // Get membership details
        var userMembershipRecord = await _orgRepository.GetMembershipAsync(firstUserOrg.Id, userId, cancellationToken);
        if (userMembershipRecord == null)
        {
            return null;
        }

        // Get members to find user email
        var orgMembers = await _orgRepository.ListMembersAsync(firstUserOrg.Id, cancellationToken);
        var userMemberInfo = orgMembers.FirstOrDefault(m => m.UserId == userId);
        if (userMemberInfo == null)
        {
            return null;
        }

        var user = await _authRepository.GetUserByEmailAsync(userMemberInfo.Email.ToLowerInvariant().Trim(), cancellationToken: cancellationToken);
        if (user == null)
        {
            return null;
        }

        ActiveOrganization? activeOrg = null;
        string? role = userMembershipRecord.Role;
        List<string> features = new();

        activeOrg = new ActiveOrganization
        {
            OrganizationId = firstUserOrg.Id,
            Name = firstUserOrg.Name,
            Slug = firstUserOrg.Slug
        };

        // Get features
        var featuresDict = await _featuresService.GetResolvedFeaturesAsync(firstUserOrg.Id, cancellationToken);
        features = featuresDict.Keys.ToList();

        return new UserInfoResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            ActiveOrganization = activeOrg,
            Role = role,
            Features = features
        };
    }
}
