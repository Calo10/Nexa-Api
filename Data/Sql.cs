using System.Security.Cryptography;
using System.Text;

namespace NexaApi.Data;

public static class Sql
{
    public static string HashToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        }

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GenerateSecureToken(int bytes = 32)
    {
        if (bytes <= 0)
        {
            throw new ArgumentException("Bytes must be greater than zero", nameof(bytes));
        }

        var randomBytes = new byte[bytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var base64 = Convert.ToBase64String(randomBytes);
        // Convert base64 to base64url: replace + with -, / with _, and remove padding
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

