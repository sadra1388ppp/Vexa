using Argon2id.PasswordHasher;
using Microsoft.AspNetCore.Identity;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

/// <summary>
/// Centralizes password hashing and verification for NovaChat.
/// New passwords use Argon2id. Existing ASP.NET Identity/PBKDF2 hashes
/// are still accepted and transparently upgraded after a successful login.
/// </summary>
public sealed class PasswordHashService
{
    private const string Argon2IdPrefix = "$argon2id$";

    private readonly Argon2idPasswordHasher _argon2id = new();
    private readonly PasswordHasher<User> _legacyHasher = new();

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _argon2id.HashPassword(password);
    }

    public bool VerifyPassword(
        User user,
        string storedHash,
        string providedPassword,
        out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrWhiteSpace(storedHash) || providedPassword is null)
            return false;

        if (storedHash.StartsWith(Argon2IdPrefix, StringComparison.Ordinal))
        {
            var result = _argon2id.Verify(providedPassword, storedHash);
            if (!result.Success)
                return false;

            needsRehash = result.NeedsRehash;
            return true;
        }

        // Backward compatibility for passwords created with the previous
        // ASP.NET Core PasswordHasher (PBKDF2). A successful legacy login
        // is immediately upgraded to Argon2id by the caller.
        var legacyResult = _legacyHasher.VerifyHashedPassword(
            user,
            storedHash,
            providedPassword);

        if (legacyResult == PasswordVerificationResult.Failed)
            return false;

        needsRehash = true;
        return true;
    }
}
