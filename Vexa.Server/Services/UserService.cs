using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using System.Text.RegularExpressions;

namespace NovaChat.Server.Services;

public class UserService
{
    private static readonly SemaphoreSlim RegisterLock = new(1, 1);
    private static readonly Regex UsernameRegex = new("^[a-zA-Z0-9_.-]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new("^0[0-9]{10}$", RegexOptions.Compiled);
    private readonly AppDbContext _context;
    private readonly PasswordHashService _passwordHashService;
    private readonly PresenceService _presenceService;

    public UserService(AppDbContext context, PasswordHashService passwordHashService, PresenceService presenceService)
    {
        _context = context;
        _passwordHashService = passwordHashService;
        _presenceService = presenceService;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterDto dto)
    {
        var username = dto.Username.Trim().ToLowerInvariant();
        var email = dto.Email.Trim();
        var displayName = dto.DisplayName.Trim();
        var messagePrivacy = NormalizeMessagePrivacy(dto.MessagePrivacy);
        if (!UsernameRegex.IsMatch(username)) return Fail("Username must be 3 to 32 characters and may contain only letters, numbers, dot, underscore and hyphen.");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(dto.Password)) return Fail("All registration fields are required.");
        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber)) return Fail("Phone number must contain exactly 11 digits and start with 0.");
        await RegisterLock.WaitAsync();
        try
        {
            if (await _context.Users.AnyAsync(u => u.Username == username)) return Fail("This username is already taken.");
            if (await _context.Users.AnyAsync(u => u.Email == email)) return Fail("This Email is already registered.");
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == phoneNumber)) return Fail("This phone number is already registered.");
            var userId = await GenerateNextUserIdAsync();
            var createdAt = IranTime.Now;
            var passwordHash = _passwordHashService.HashPassword(dto.Password);
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO `Users` (`Id`, `Username`, `DisplayName`, `Email`, `PhoneNumber`, `PasswordHash`, `Bio`, `AvatarUrl`, `LastSeenAt`, `CreatedAt`, `MessagePrivacy`, `AllowGroupAdds`)
                VALUES ({userId}, {username}, {displayName}, {email}, {phoneNumber}, {passwordHash}, {string.Empty}, {null}, {null}, {createdAt}, {messagePrivacy}, {dto.AllowGroupAdds})");
            var user = new User
            {
                Id = userId,
                Username = username,
                DisplayName = displayName,
                Email = email,
                PhoneNumber = phoneNumber,
                PasswordHash = passwordHash,
                Bio = string.Empty,
                AvatarUrl = null,
                LastSeenAt = null,
                CreatedAt = createdAt,
                MessagePrivacy = messagePrivacy,
                AllowGroupAdds = dto.AllowGroupAdds
            };
            return new RegisterResult { Success = true, Message = "User registered successfully.", User = ToUserResponse(user) };
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: 1062 })
        {
            _context.ChangeTracker.Clear();
            return Fail("This username, email, or phone number is already registered.");
        }
        finally { RegisterLock.Release(); }
    }

    public async Task<(bool UsernameTaken, bool EmailTaken, bool PhoneTaken)> CheckRegistrationAvailabilityAsync(string? username, string? email, string? phoneNumber)
    {
        username = username?.Trim().ToLowerInvariant(); email = email?.Trim();
        var normalizedPhone = TryNormalizePhoneNumber(phoneNumber, out var phone) ? phone : null;
        var usernameTaken = !string.IsNullOrWhiteSpace(username) && await _context.Users.AsNoTracking().AnyAsync(u => u.Username == username);
        var emailTaken = !string.IsNullOrWhiteSpace(email) && await _context.Users.AsNoTracking().AnyAsync(u => u.Email == email);
        var phoneTaken = normalizedPhone != null && await _context.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == normalizedPhone);
        return (usernameTaken, emailTaken, phoneTaken);
    }

    public async Task<User?> LoginAsync(LoginDto dto)
    {
        var login = dto.Login.Trim();
        if (string.IsNullOrWhiteSpace(login)) return null;
        User? user = null;
        if (TryNormalizePhoneNumber(login, out var phoneNumber)) user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        if (user == null) user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login.ToLowerInvariant());
        if (user == null) user = await _context.Users.FirstOrDefaultAsync(u => u.Email == login);
        if (user == null) return null;
        if (!_passwordHashService.VerifyPassword(user, user.PasswordHash, dto.Password, out var needsRehash)) return null;
        if (needsRehash) { user.PasswordHash = _passwordHashService.HashPassword(dto.Password); await _context.SaveChangesAsync(); }
        return user;
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(string id, bool includePhoneNumber = false)
    {
        if (!long.TryParse(id, out var userId)) return null;
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user == null ? null : ToUserResponse(user, includePhoneNumber);
    }

    public async Task<List<UserResponseDto>> SearchUsersAsync(string query, string currentUserId)
    {
        query = query.Trim(); if (query.Length < 1) return [];
        long.TryParse(currentUserId, out var excludedId);
        var pattern = $"%{query}%";
        var users = await _context.Users.AsNoTracking().Where(u => u.Id != excludedId && (EF.Functions.Like(u.Username, pattern) || EF.Functions.Like(u.DisplayName, pattern) || EF.Functions.Like(u.Email, pattern))).OrderBy(u => u.DisplayName).Take(30).ToListAsync();
        return users.Select(user => ToUserResponse(user)).ToList();
    }

    public async Task<RegisterResult> UpdateUserAsync(string id, UpdateUserDto dto)
    {
        if (!long.TryParse(id, out var userId)) return Fail("User not found.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return Fail("User not found.");
        var newUsername = string.IsNullOrWhiteSpace(dto.NewUsername) ? user.Username : dto.NewUsername.Trim().ToLowerInvariant();
        if (!UsernameRegex.IsMatch(newUsername)) return Fail("Username must be 3 to 32 characters and may contain only letters, numbers, dot, underscore and hyphen.");
        dto.DisplayName = dto.DisplayName.Trim(); dto.Email = dto.Email.Trim(); dto.Bio = (dto.Bio ?? string.Empty).Trim();
        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber)) return Fail("Phone number must contain exactly 11 digits and start with 0.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Username == newUsername && u.Id != userId)) return Fail("This username is already taken.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Email == dto.Email && u.Id != userId)) return Fail("This Email is already registered.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phoneNumber && u.Id != userId)) return Fail("This phone number is already registered.");
        var oldUsername = user.Username;
        user.Username = newUsername; user.DisplayName = dto.DisplayName; user.Email = dto.Email; user.PhoneNumber = phoneNumber; user.Bio = dto.Bio;
        await _context.SaveChangesAsync();
        if (!string.Equals(oldUsername, newUsername, StringComparison.Ordinal))
        {
            var chats = await _context.Chats.ToListAsync();
            foreach (var chat in chats)
            {
                var members = ParseMembers(chat.Members); var changed = false;
                for (var i = 0; i < members.Count; i++) if (string.Equals(members[i], oldUsername, StringComparison.OrdinalIgnoreCase)) { members[i] = newUsername; changed = true; }
                if (changed) chat.Members = string.Join(", ", members.Distinct(StringComparer.OrdinalIgnoreCase));
            }
            await _context.SaveChangesAsync();
        }
        return new RegisterResult { Success = true, Message = "User updated successfully.", User = ToUserResponse(user, true) };
    }

    public async Task<(bool Success, string Message, UserResponseDto? User)> SetAvatarAsync(string id, string avatarUrl)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.", null);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.", null);
        user.AvatarUrl = avatarUrl; await _context.SaveChangesAsync(); return (true, "Profile picture updated successfully.", ToUserResponse(user, true));
    }

    public async Task<(bool Success, string Message, string? OldAvatarUrl)> ClearAvatarAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.", null);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.", null);
        var old = user.AvatarUrl; user.AvatarUrl = null; await _context.SaveChangesAsync(); return (true, "Profile picture removed successfully.", old);
    }

    public async Task MarkLastSeenAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return;
        var lastSeen = IranTime.Now;
        await _context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, lastSeen));
    }

    public async Task<bool> DeleteUserAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return false;

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Username })
            .FirstOrDefaultAsync();

        if (user == null) return false;

        // MariaDB retry execution strategies do not allow a manually-created
        // transaction outside the execution strategy. The whole delete operation
        // must therefore run inside CreateExecutionStrategy().ExecuteAsync().
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Remove explicit read receipts first for compatibility with older schemas.
                try
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM MessageReads WHERE UserId = {userId}");
                }
                catch (MySqlException exception) when (exception.Number == 1146)
                {
                    // Older local databases may not have MessageReads.
                }

                // Delete messages sent by the account.
                await _context.Messages
                    .Where(m => m.SenderId == user.Username)
                    .ExecuteDeleteAsync();

                // Delete contacts involving the account.
                await _context.Contacts
                    .Where(c => c.OwnerUserId == userId || c.ContactUserId == userId)
                    .ExecuteDeleteAsync();

                // Delete chats created by the account. Messages are removed first
                // so this also works with older databases lacking cascade rules.
                var ownedChatIds = await _context.Chats
                    .Where(c => c.CreatedByUserId == userId)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (ownedChatIds.Count > 0)
                {
                    await _context.Messages
                        .Where(m => ownedChatIds.Contains(m.ChatId))
                        .ExecuteDeleteAsync();

                    await _context.Chats
                        .Where(c => ownedChatIds.Contains(c.Id))
                        .ExecuteDeleteAsync();
                }

                // Keep chats owned by other users and remove this username
                // from their participant lists.
                var remainingChats = await _context.Chats.ToListAsync();
                foreach (var chat in remainingChats)
                {
                    var members = ParseMembers(chat.Members);
                    var removed = members.RemoveAll(m =>
                        string.Equals(m, user.Username, StringComparison.OrdinalIgnoreCase));

                    if (removed > 0)
                        chat.Members = string.Join(", ", members.Distinct(StringComparer.OrdinalIgnoreCase));
                }

                await _context.SaveChangesAsync();

                var deleted = await _context.Users
                    .Where(u => u.Id == userId)
                    .ExecuteDeleteAsync();

                if (deleted == 0)
                {
                    await transaction.RollbackAsync();
                    return false;
                }

                await transaction.CommitAsync();
                _context.ChangeTracker.Clear();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string id, ChangePasswordDto dto)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.");
        if (!_passwordHashService.VerifyPassword(user, user.PasswordHash, dto.CurrentPassword, out _)) return (false, "Current password is incorrect.");
        user.PasswordHash = _passwordHashService.HashPassword(dto.NewPassword); await _context.SaveChangesAsync(); return (true, "Password changed successfully.");
    }

    private async Task<long> GenerateNextUserIdAsync()
    {
        var maxId = await _context.Users.AsNoTracking().MaxAsync(u => (long?)u.Id) ?? 0L;
        if (maxId == long.MaxValue) throw new InvalidOperationException("No more user IDs are available."); return maxId + 1;
    }

    private static string NormalizeMessagePrivacy(string? value) => string.Equals(value?.Trim(), "Requests", StringComparison.OrdinalIgnoreCase) ? "Requests" : "Everybody";
    private static List<string> ParseMembers(string? members) => string.IsNullOrWhiteSpace(members) ? [] : members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    private static RegisterResult Fail(string message) => new() { Success = false, Message = message };
    private UserResponseDto ToUserResponse(User user, bool includePhoneNumber = false) => new() { Id = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Username = user.Username, DisplayName = user.DisplayName, Email = user.Email, PhoneNumber = includePhoneNumber ? user.PhoneNumber : null, Bio = user.Bio, AvatarUrl = user.AvatarUrl, IsOnline = _presenceService.IsOnline(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), AllowGroupAdds = user.AllowGroupAdds, LastSeenAt = user.LastSeenAt, CreatedAt = user.CreatedAt };
    private static bool TryNormalizePhoneNumber(string? input, out string normalized)
    {
        normalized = string.Empty; if (string.IsNullOrWhiteSpace(input)) return false;
        normalized = input.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty); return PhoneRegex.IsMatch(normalized);
    }
}