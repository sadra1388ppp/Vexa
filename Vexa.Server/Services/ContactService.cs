using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ContactService
{
    private readonly AppDbContext _context;
    public ContactService(AppDbContext context) => _context = context;

    public async Task<(bool Success, string Message)> AddAsync(string ownerUserId, string contactUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId) || !long.TryParse(contactUserId?.Trim(), out var contactId))
            return (false, "Valid numeric user ID is required.");
        if (ownerId == contactId) return (false, "You cannot add yourself as a contact.");
        if (!await _context.Users.AnyAsync(u => u.Id == contactId)) return (false, "User not found.");
        if (await _context.Contacts.AnyAsync(c => c.OwnerUserId == ownerId && c.ContactUserId == contactId))
            return (false, "This user is already in your contacts.");

        _context.Contacts.Add(new Contact
        {
            OwnerUserId = ownerId,
            ContactUserId = contactId,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return (true, "Contact added successfully.");
    }

    public async Task<List<ContactResponseDto>> GetAllAsync(string ownerUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId)) return [];

        return await _context.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerId)
            .OrderBy(c => c.ContactUser.DisplayName)
            .Select(c => new ContactResponseDto
            {
                UserId = c.ContactUserId.ToString(),
                Username = c.ContactUser.Username,
                DisplayName = c.ContactUser.DisplayName,
                Email = c.ContactUser.Email,
                AddedAt = c.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<List<ContactResponseDto>> GetGroupCandidatesAsync(string ownerUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId)) return [];

        var contacts = await _context.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerId)
            .Select(c => new ContactResponseDto
            {
                UserId = c.ContactUserId.ToString(),
                Username = c.ContactUser.Username,
                DisplayName = c.ContactUser.DisplayName,
                Email = c.ContactUser.Email,
                AddedAt = c.CreatedAt
            })
            .ToListAsync();

        var ownerUsername = await _context.Users.AsNoTracking()
            .Where(u => u.Id == ownerId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync() ?? string.Empty;

        // Chats.Members is the single source of membership now. Load private chats
        // and resolve the other username in memory to keep the query MariaDB-safe.
        var privateChats = await _context.Chats.AsNoTracking()
            .Where(c => c.Type == ChatType.Private)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var usernameRows = privateChats
            .SelectMany(chat => ParseMembers(chat.Members)
                .Where(username => !string.Equals(username, ownerUsername, StringComparison.OrdinalIgnoreCase))
                .Select(username => new { Username = username, chat.CreatedAt }))
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToList();

        var recentUsernames = usernameRows
            .Select(x => x.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recentUsernames.Count == 0)
            return contacts.OrderBy(x => x.DisplayName).ThenBy(x => x.Username).ToList();

        var recentUsers = await _context.Users.AsNoTracking()
            .Where(u => recentUsernames.Contains(u.Username))
            .ToListAsync();

        var recentChats = recentUsers.Select(user =>
        {
            var recent = usernameRows.First(x => string.Equals(x.Username, user.Username, StringComparison.OrdinalIgnoreCase));
            return new ContactResponseDto
            {
                UserId = user.Id.ToString(),
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                AddedAt = recent.CreatedAt
            };
        });

        return contacts
            .Concat(recentChats)
            .GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.AddedAt).First())
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Username)
            .ToList();
    }

    public async Task<bool> RemoveAsync(string ownerUserId, string contactUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId) || !long.TryParse(contactUserId, out var contactId)) return false;
        var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == ownerId && c.ContactUserId == contactId);
        if (contact == null) return false;
        _context.Contacts.Remove(contact);
        await _context.SaveChangesAsync();
        return true;
    }

    private static List<string> ParseMembers(string? members) =>
        string.IsNullOrWhiteSpace(members)
            ? []
            : members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
