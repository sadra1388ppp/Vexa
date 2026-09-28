using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace NovaChat.Server.Services;

public sealed class JwtTokenRevocationService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public void Revoke(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var expiration = GetExpiration(token) ?? DateTimeOffset.UtcNow.AddHours(1);
        _revokedTokens[HashToken(token)] = expiration;
        CleanupExpired();
    }

    public bool IsRevoked(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        CleanupExpired();
        return _revokedTokens.ContainsKey(HashToken(token));
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in _revokedTokens)
        {
            if (entry.Value <= now)
                _revokedTokens.TryRemove(entry.Key, out _);
        }
    }

    private static DateTimeOffset? GetExpiration(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue
                ? null
                : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
