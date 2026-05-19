using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NexaApi.Data;
using System.Text;

namespace NexaApi.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IConfiguration _configuration;

    public HealthController(IDbConnectionFactory dbConnectionFactory, IConfiguration configuration)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _configuration = configuration;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get()
    {
        try
        {
            // Test database connection
            using var connection = _dbConnectionFactory.CreateConnection();
            await connection.OpenAsync();
            
            // Get database information
            using var dbInfoCommand = connection.CreateCommand();
            dbInfoCommand.CommandText = @"
                SELECT 
                    DB_NAME() AS DatabaseName,
                    @@VERSION AS SqlServerVersion";
            
            using var dbInfoReader = await dbInfoCommand.ExecuteReaderAsync();
            string databaseName = "";
            string sqlServerVersion = "";
            
            if (await dbInfoReader.ReadAsync())
            {
                databaseName = dbInfoReader.GetString(0);
                sqlServerVersion = dbInfoReader.GetString(1);
            }
            await dbInfoReader.CloseAsync();
            
            // Get server name from connection string (more reliable for Azure SQL)
            var serverName = connection.DataSource ?? "unknown";
            
            // Get table counts
            using var countsCommand = connection.CreateCommand();
            countsCommand.CommandText = @"
                SELECT 
                    (SELECT COUNT(*) FROM users WHERE deleted_at IS NULL) AS UsersCount,
                    (SELECT COUNT(*) FROM organizations WHERE deleted_at IS NULL) AS OrganizationsCount,
                    (SELECT COUNT(*) FROM subscriptions WHERE status = 'active') AS ActiveSubscriptionsCount,
                    (SELECT COUNT(*) FROM sessions WHERE revoked_at IS NULL AND expires_at > GETUTCDATE()) AS ActiveSessionsCount";
            
            using var countsReader = await countsCommand.ExecuteReaderAsync();
            int usersCount = 0;
            int organizationsCount = 0;
            int activeSubscriptionsCount = 0;
            int activeSessionsCount = 0;
            
            if (await countsReader.ReadAsync())
            {
                usersCount = countsReader.GetInt32(0);
                organizationsCount = countsReader.GetInt32(1);
                activeSubscriptionsCount = countsReader.GetInt32(2);
                activeSessionsCount = countsReader.GetInt32(3);
            }
            
            return Ok(new { 
                status = "ok",
                database = new {
                    connected = true,
                    name = databaseName,
                    server = serverName,
                    version = sqlServerVersion.Split('\n').FirstOrDefault()?.Trim() ?? sqlServerVersion,
                    timestamp = DateTimeOffset.UtcNow
                },
                counts = new {
                    users = usersCount,
                    organizations = organizationsCount,
                    activeSubscriptions = activeSubscriptionsCount,
                    activeSessions = activeSessionsCount
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { 
                status = "unhealthy",
                database = new {
                    connected = false,
                    error = ex.Message
                },
                timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    [HttpGet("debug-token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DebugToken([FromHeader(Name = "Authorization")] string? authorization)
    {
        if (string.IsNullOrEmpty(authorization))
        {
            return BadRequest(new { error = "No Authorization header provided" });
        }

        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization.Substring("Bearer ".Length).Trim()
            : authorization.Trim();

        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");
        var issuer = jwtSettings["Issuer"] ?? "NexaApi";
        var audience = jwtSettings["Audience"] ?? "NexaApi";

        var tokenHandler = new JwtSecurityTokenHandler();
        
        try
        {
            // Try to read token without validation first
            var jsonToken = tokenHandler.ReadJwtToken(token);
            
            var decodedTokenInfo = new
            {
                issuer = jsonToken.Issuer,
                audience = jsonToken.Audiences?.FirstOrDefault(),
                expires = jsonToken.ValidTo,
                issuedAt = jsonToken.IssuedAt,
                claims = jsonToken.Claims.Select(c => new { c.Type, c.Value }).ToList()
            };

            var expectedConfig = new
            {
                issuer = issuer,
                audience = audience,
                keyLength = secretKey.Length
            };

            var isExpired = jsonToken.ValidTo < DateTime.UtcNow;
            var issuerMatches = jsonToken.Issuer == issuer;
            var audienceMatches = jsonToken.Audiences?.Contains(audience) == true;

            // Now try to validate
            object validationResult;
            try
            {
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                validationResult = new
                {
                    isValid = true,
                    isExpired = isExpired,
                    issuerMatches = issuerMatches,
                    audienceMatches = audienceMatches
                };
            }
            catch (Exception validationEx)
            {
                validationResult = new
                {
                    isValid = false,
                    error = validationEx.GetType().Name,
                    errorMessage = validationEx.Message,
                    isExpired = isExpired,
                    issuerMatches = issuerMatches,
                    audienceMatches = audienceMatches
                };
            }

            var result = new
            {
                tokenReceived = true,
                tokenPreview = token.Length > 50 ? token.Substring(0, 50) + "..." : token,
                tokenLength = token.Length,
                decodedToken = decodedTokenInfo,
                expectedConfig = expectedConfig,
                validation = validationResult
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                tokenReceived = true,
                tokenPreview = token.Length > 50 ? token.Substring(0, 50) + "..." : token,
                tokenLength = token.Length,
                error = ex.GetType().Name,
                errorMessage = ex.Message,
                expectedConfig = new
                {
                    issuer = issuer,
                    audience = audience,
                    keyLength = secretKey.Length
                }
            });
        }
    }
}

