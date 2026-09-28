using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;
    private readonly UserService _userService;

    private const string EncryptedMessagePlaceholder = "[Encrypted message — content unavailable to administrators]";

    public AdminController(AppDbContext db, IHubContext<ChatHub> hub, UserService userService)
    {
        _db = db;
        _hub = hub;
        _userService = userService;
    }

    [HttpGet("test")]
    public IActionResult Test() => Ok(new { message = "Owner access granted." });

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers() => Ok(await _db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.CreatedAt }).OrderBy(u => u.CreatedAt).ToListAsync());

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.Email,
                u.PhoneNumber,
                u.Bio,
                u.AvatarUrl,
                u.LastSeenAt,
                u.CreatedAt,
                u.MessagePrivacy,
                u.AllowGroupAdds,
                PasswordStatus = "Protected — password is not retrievable"
            })
            .FirstOrDefaultAsync();

        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound(new { message = "User not found." });

        var username = string.IsNullOrWhiteSpace(dto.NewUsername) ? user.Username : dto.NewUsername.Trim().ToLowerInvariant();
        var displayName = dto.DisplayName.Trim();
        var email = dto.Email.Trim();
        var phone = NormalizePhone(dto.PhoneNumber);
        var bio = (dto.Bio ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Display Name and Email are required." });
        if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "Username is required." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username && u.Id != userId)) return Conflict(new { message = "This Username is already taken." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != userId)) return Conflict(new { message = "This Email is already registered." });
        if (!string.IsNullOrWhiteSpace(phone) && await _db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phone && u.Id != userId)) return Conflict(new { message = "This Phone Number is already registered." });

        user.Username = username;
        user.DisplayName = displayName;
        user.Email = email;
        user.PhoneNumber = phone;
        user.Bio = bio;
        await _db.SaveChangesAsync();

        return Ok(new { message = "User updated successfully.", user = new { user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, user.Bio, user.AvatarUrl, user.CreatedAt } });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        if (long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId) && ownerId == userId) return BadRequest(new { message = "Owner cannot delete the Owner account." });
        var deleted = await _userService.DeleteUserAsync(id);
        return deleted ? Ok(new { message = "User deleted successfully." }) : NotFound(new { message = "User not found." });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview() => Ok(new { status = "Online", users = await _db.Users.CountAsync(), serverTime = IranTime.Now });

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(new { serverName = "NovaChat Server", ownerAccess = true, status = "Online" });

    [HttpGet("users/{id}/chats")]
    public async Task<IActionResult> GetUserChats(string id)
    {
        try
        {
            if (!long.TryParse(id, out var userId) || userId <= 0)
                return BadRequest(new { message = "Invalid user ID." });

            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Username })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { message = "User not found." });

            var chats = await _db.Chats.AsNoTracking()
                .Where(c => !c.IsDeleted &&
                    (c.Members == user.Username ||
                     c.Members.StartsWith(user.Username + ", ") ||
                     c.Members.Contains(", " + user.Username + ", ") ||
                     c.Members.EndsWith(", " + user.Username)))
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .ToListAsync();

            var result = new List<AdminChatDto>(chats.Count);
            foreach (var chat in chats)
            {
                var memberNames = ParseMembers(chat.Members).ToList();
                var otherUsername = chat.Type == ChatType.Private
                    ? memberNames.FirstOrDefault(x => !string.Equals(x, user.Username, StringComparison.OrdinalIgnoreCase))
                    : null;

                var other = !string.IsNullOrWhiteSpace(otherUsername)
                    ? await _db.Users.AsNoTracking()
                        .Where(u => u.Username == otherUsername)
                        .Select(u => new { u.Id, u.Username, u.DisplayName, u.AvatarUrl })
                        .FirstOrDefaultAsync()
                    : null;

                var last = await _db.Messages.AsNoTracking()
                    .Where(m => m.ChatId == chat.Id && !m.DeletedForEveryone)
                    .OrderByDescending(m => m.SentAt)
                    .ThenByDescending(m => m.Id)
                    .FirstOrDefaultAsync();

                var count = await _db.Messages.AsNoTracking()
                    .CountAsync(m => m.ChatId == chat.Id && !m.DeletedForEveryone);

                result.Add(new AdminChatDto
                {
                    Id = chat.Id,
                    Type = chat.Type,
                    ChatName = chat.Name,
                    OtherUserId = other?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    OtherUsername = other?.Username ?? string.Empty,
                    OtherDisplayName = other?.DisplayName ?? string.Empty,
                    OtherAvatarUrl = other?.AvatarUrl,
                    CreatedAt = chat.CreatedAt,
                    MessageCount = count,
                    LastMessage = last == null ? null : MapAdminSafeMessage(last)
                });
            }

            return Ok(result
                .OrderByDescending(x => x.LastMessage?.SentAt ?? x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .ToList());
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 500, title: "Could not load user chats.", detail: "The owner chat viewer could not read this user's conversation list safely.");
        }
    }

    [HttpGet("chats/{chatId}/messages")]
    public async Task<IActionResult> GetAdminChatMessages(int chatId)
    {
        if (chatId <= 0) return BadRequest(new { message = "Invalid chat ID." });
        var exists = await _db.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId && !c.IsDeleted);
        if (!exists) return NotFound(new { message = "Chat not found." });

        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        return Ok(messages.Select(MapAdminSafeMessage).ToList());
    }

    [HttpPut("messages/{messageId}")]
    public IActionResult EditMessage(int messageId, [FromBody] AdminEditMessageDto dto)
        => StatusCode(StatusCodes.Status403Forbidden, new { message = "Administrators cannot edit encrypted message contents." });

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        if (messageId <= 0) return BadRequest(new { message = "Invalid message ID." });
        var message = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.ChatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });

        var payload = new
        {
            id = message.Id,
            chatId = message.ChatId,
            senderId = message.SenderId,
            content = EncryptedMessagePlaceholder,
            sentAt = message.SentAt
        };

        _db.Messages.Remove(message);
        await _db.SaveChangesAsync();
        await SendToChatMembersAsync(chat, "MessageDeleted", payload);
        return Ok(new { message = "Message deleted successfully." });
    }

    private static MessageDto MapAdminSafeMessage(Message message)
    {
        var dto = MessageDtoMapper.Map(message);
        dto.Content = EncryptedMessagePlaceholder;
        return dto;
    }

    private async Task SendToChatMembersAsync(Chat chat, string method, object payload)
    {
        var usernames = ParseMembers(chat.Members).ToArray();
        if (usernames.Length == 0) return;
        var ids = await _db.Users.AsNoTracking()
            .Where(u => usernames.Contains(u.Username))
            .Select(u => u.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArrayAsync();
        if (ids.Length > 0) await _hub.Clients.Users(ids).SendAsync(method, payload);
    }

    private static IEnumerable<string> ParseMembers(string? members) =>
        (members ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);

    public sealed class AdminChatDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string ChatName { get; set; } = string.Empty;
        public string OtherUserId { get; set; } = string.Empty;
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherDisplayName { get; set; } = string.Empty;
        public string? OtherAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int MessageCount { get; set; }
        public MessageDto? LastMessage { get; set; }
    }


    public sealed class AdminEditMessageDto
    {
        public string Content { get; set; } = string.Empty;
    }
}
