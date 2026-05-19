using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NexaApi.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _key;
    private readonly int _accessTokenMinutes;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IConfiguration configuration)
    {
        // Use JwtSettings to match Program.cs configuration
        var jwtSettings = configuration.GetSection("JwtSettings");
        var jwtLegacy = configuration.GetSection("Jwt"); // Fallback for legacy config
        
        _issuer = jwtSettings["Issuer"] ?? jwtLegacy["Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured");
        _audience = jwtSettings["Audience"] ?? jwtLegacy["Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured");
        _key = jwtSettings["SecretKey"] ?? jwtLegacy["Key"] ?? throw new InvalidOperationException("JWT SecretKey/Key is not configured");
        
        var accessTokenMinutesStr = jwtSettings["AccessTokenExpirationMinutes"] ?? jwtLegacy["AccessTokenMinutes"];
        if (!int.TryParse(accessTokenMinutesStr, out _accessTokenMinutes) || _accessTokenMinutes <= 0)
        {
            throw new InvalidOperationException("JWT AccessTokenExpirationMinutes/AccessTokenMinutes must be a positive integer");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
    }

    public string GenerateToken(Guid userId, Guid? orgId = null, Guid? membershipId = null, string? role = null, string? featuresJson = null)
    {
        var claims = new List<Claim>
        {
            new Claim("sub", userId.ToString())
        };

        if (orgId.HasValue)
        {
            claims.Add(new Claim("org_id", orgId.Value.ToString()));
        }

        if (membershipId.HasValue)
        {
            claims.Add(new Claim("membership_id", membershipId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim("role", role));
        }

        if (!string.IsNullOrWhiteSpace(featuresJson))
        {
            claims.Add(new Claim("features", featuresJson));
        }

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

