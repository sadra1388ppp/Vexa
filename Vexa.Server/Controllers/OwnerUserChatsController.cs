using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using NovaChat.Server.DTOs;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/OwnerUser")]
[Authorize(Policy = "OwnerOnly")]
public sealed class OwnerUserChatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public OwnerUserChatsController(AppDbContext db) => _db = db;

    [HttpGet("{userId:long}/chats")]
    public async Task<IActionResult> GetUserChats(long userId)
    {
        if (userId <= 0)
            return BadRequest(new { message = "Invalid user ID." });

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Username, u.DisplayName })
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound(new { message = "User not found." });

        var username = user.Username;
        var chats = await _db.Chats.AsNoTracking()
            .Where(c => !c.IsDeleted &&
                (c.Members == username ||
                 c.Members.StartsWith(username + ", ") ||
                 c.Members.Contains(", " + username + ", ") ||
                 c.Members.EndsWith(", " + username)))
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        var otherNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chat in chats)
        {
            foreach (var member in ParseMembers(chat.Members))
            {
                if (!string.Equals(member, username, StringComparison.OrdinalIgnoreCase))
                    otherNames.Add(member);
            }
        }

        var users = otherNames.Count == 0
            ? []
            : await _db.Users.AsNoTracking()
                .Where(u => otherNames.Contains(u.Username))
                .Select(u => new { u.Id, u.Username, u.DisplayName, u.AvatarUrl })
                .ToListAsync();

        var usersByName = users.ToDictionary(x => x.Username, StringComparer.OrdinalIgnoreCase);
        var result = new List<OwnerUserChatDto>(chats.Count);

        foreach (var chat in chats)
        {
            var members = ParseMembers(chat.Members).ToList();
            var otherUsername = chat.Type == ChatType.Private
                ? members.FirstOrDefault(x => !string.Equals(x, username, StringComparison.OrdinalIgnoreCase))
                : null;
            var other = !string.IsNullOrWhiteSpace(otherUsername) && usersByName.TryGetValue(otherUsername, out var otherUser)
                ? otherUser
                : null;

            var last = await _db.Messages.AsNoTracking()
                .Where(m => m.ChatId == chat.Id && !m.DeletedForEveryone)
                .OrderByDescending(m => m.SentAt)
                .ThenByDescending(m => m.Id)
                .Select(m => new OwnerUserMessageDto
                {
                    Id = m.Id,
                    SentAt = m.SentAt,
                    SenderId = m.SenderId,
                    Content = "[Encrypted message — content unavailable to administrators]"
                })
                .FirstOrDefaultAsync();

            var count = await _db.Messages.AsNoTracking()
                .CountAsync(m => m.ChatId == chat.Id && !m.DeletedForEveryone);

            result.Add(new OwnerUserChatDto
            {
                Id = chat.Id,
                Type = chat.Type,
                Name = chat.Name,
                CreatedAt = chat.CreatedAt,
                MemberCount = members.Count,
                MessageCount = count,
                OtherUserId = other?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                OtherUsername = other?.Username ?? string.Empty,
                OtherDisplayName = other?.DisplayName ?? string.Empty,
                OtherAvatarUrl = other?.AvatarUrl,
                LastMessage = last
            });
        }

        return Ok(result
            .OrderByDescending(x => x.LastMessage?.SentAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToList());
    }

    [HttpGet("{userId:long}/chats/{chatId:int}/messages")]
    public async Task<IActionResult> GetUserChatMessages(long userId, int chatId)
    {
        if (userId <= 0 || chatId <= 0)
            return BadRequest(new { message = "Invalid user or chat ID." });

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Username })
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound(new { message = "User not found." });

        var chat = await _db.Chats.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);

        if (chat == null)
            return NotFound(new { message = "Chat not found." });

        if (!ParseMembers(chat.Members).Any(x => string.Equals(x, user.Username, StringComparison.OrdinalIgnoreCase)))
            return Forbid();

        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Select(m => new OwnerUserMessageDto
            {
                Id = m.Id,
                SentAt = m.SentAt,
                SenderId = m.SenderId,
                Content = "[Encrypted message — content unavailable to administrators]"
            })
            .ToListAsync();

        return Ok(messages);
    }

    private static IEnumerable<string> ParseMembers(string? members) =>
        (members ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private sealed class OwnerUserChatDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int MemberCount { get; set; }
        public int MessageCount { get; set; }
        public string OtherUserId { get; set; } = string.Empty;
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherDisplayName { get; set; } = string.Empty;
        public string? OtherAvatarUrl { get; set; }
        public OwnerUserMessageDto? LastMessage { get; set; }
    }

    private sealed class OwnerUserMessageDto
    {
        public int Id { get; set; }
        public DateTime SentAt { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
