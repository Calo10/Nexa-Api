using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaApi.DTOs.Auth;
using NexaApi.Services;
using System.Security.Claims;

namespace NexaApi.Controllers;

[ApiController]
[Route("v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("magic-link")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendMagicLink([FromBody] MagicLinkRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        // Validate email format
        try
        {
            var emailAddress = new System.Net.Mail.MailAddress(request.Email);
            if (emailAddress.Address != request.Email.Trim())
            {
                return BadRequest(new { error = "Invalid email format" });
            }
        }
        catch
        {
            return BadRequest(new { error = "Invalid email format" });
        }

        try
        {
            var result = await _authService.SendMagicLinkAsync(request.Email, cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new { error = "Failed to send magic link" });
            }

            // In Development, return token for testing
            if (result.Token != null)
            {
                return Ok(new
                {
                    message = "Magic link generated successfully (Development mode - token returned)",
                    token = result.Token,
                    expiresAt = result.ExpiresAt,
                    note = "Use this token in POST /v1/auth/consume"
                });
            }

            return Ok(new { message = "Magic link sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending magic link to {Email}", request.Email);
            return StatusCode(500, new { error = "Failed to send magic link email. Please try again later." });
        }
    }

    [HttpPost("consume")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConsumeToken([FromBody] ConsumeTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Token is required" });
        }

        var userAgent = Request.Headers["User-Agent"].ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var response = await _authService.ConsumeTokenAsync(request.Token, userAgent, ip, cancellationToken);

            if (response == null)
            {
                return Unauthorized(new { error = "Invalid or expired token" });
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("password/login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PasswordLogin([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Password is required" });
        }

        var userAgent = Request.Headers["User-Agent"].ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var response = await _authService.LoginWithPasswordAsync(request.Email, request.Password, userAgent, ip, cancellationToken);

            if (response == null)
            {
                return Unauthorized(new { error = "Invalid credentials" });
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Password is required" });
        }

        var userAgent = Request.Headers["User-Agent"].ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var response = await _authService.LoginWithPasswordAsync(request.Email, request.Password, userAgent, ip, cancellationToken);

            if (response == null)
            {
                return Unauthorized(new { error = "Invalid email or password" });
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required" });
        }

        try
        {
            var response = await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);

            if (response == null)
            {
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            }

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required" });
        }

        var result = await _authService.LogoutAsync(request.RefreshToken, cancellationToken);

        if (!result)
        {
            return BadRequest(new { error = "Failed to logout" });
        }

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("password/set")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest request, CancellationToken cancellationToken)
    {
        // Get userId from JWT claims (sub)
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        // Validate newPassword and confirmPassword match
        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new { error = "New password and confirmation password do not match" });
        }

        // Validate newPassword is provided
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "New password is required" });
        }

        try
        {
            await _authService.SetPasswordAsync(userId, request.NewPassword, cancellationToken);
            return Ok(new { message = "Password set successfully" });
        }
        catch (ArgumentException ex)
        {
            // Password policy validation errors
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            // User not found/disabled
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting password for user {UserId}", userId);
            return StatusCode(500, new { error = "An error occurred while setting the password" });
        }
    }

    [HttpPost("password/initialize")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InitializePassword([FromBody] InitializePasswordRequest request, CancellationToken cancellationToken)
    {
        // Get userId from JWT claims (sub) - same way /v1/auth/me does
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        // Validate newPassword and confirmPassword match
        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new { error = "New password and confirmation password do not match" });
        }

        // Validate newPassword is provided
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "New password is required" });
        }

        try
        {
            await _authService.InitializePasswordAsync(userId, request.NewPassword, cancellationToken);
            return Ok(new { message = "Password initialized" });
        }
        catch (ArgumentException ex)
        {
            // Password policy validation errors
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            // User not found/disabled
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Password already set
            if (ex.Message == "Password already set")
            {
                return Conflict(new { error = "Password already set" });
            }
            // Other InvalidOperationException (e.g., hashing error)
            _logger.LogError(ex, "Error initializing password for user {UserId}", userId);
            return StatusCode(500, new { error = "An error occurred while initializing the password" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing password for user {UserId}", userId);
            return StatusCode(500, new { error = "An error occurred while initializing the password" });
        }
    }

    [HttpPost("password/reset/request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        try
        {
            await _authService.RequestPasswordResetAsync(request.Email, cancellationToken);
            // Always return 200 OK for security (don't reveal if user exists)
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset for {Email}", request.Email);
            // Still return 200 OK to not reveal errors
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        }
    }

    [HttpPost("password/reset/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] PasswordResetConfirmRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Token is required" });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "New password is required" });
        }

        try
        {
            await _authService.ConfirmPasswordResetAsync(request.Token, request.NewPassword, cancellationToken);
            return Ok(new { message = "Password has been reset successfully" });
        }
        catch (ArgumentException ex)
        {
            // Invalid/expired token or password policy validation
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            // User disabled
            return StatusCode(403, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming password reset");
            return BadRequest(new { error = "Invalid or expired reset token" });
        }
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserInfoResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid user identity" });
        }

        var userInfo = await _authService.GetCurrentUserAsync(userId, cancellationToken);

        if (userInfo == null)
        {
            return NotFound(new { error = "User not found" });
        }

        return Ok(userInfo);
    }
}
