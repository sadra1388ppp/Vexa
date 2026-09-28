using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class ChatController : ControllerBase
{
    private const long MaxGroupAvatarBytes = 5 * 1024 * 1024;
    private readonly ChatService _chatService;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatController> _logger;
    private readonly IWebHostEnvironment _environment;

    public ChatController(ChatService chatService, IConfiguration configuration, IHubContext<ChatHub> hub, ILogger<ChatController> logger, IWebHostEnvironment environment)
    {
        _chatService = chatService;
        _configuration = configuration;
        _hub = hub;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChat(CreateChatDto dto)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var otherUser = await _chatService.GetUserByUsernameAsync(dto.Username);
        if (otherUser == null) return NotFound(new { message = "User not found." });
        if (otherUser.Id == currentUserId) return BadRequest(new { message = "You cannot create a private chat with yourself." });
        try
        {
            var chat = await _chatService.CreatePrivateChatAsync(currentUserId, otherUser.Id);
            if (chat == null) return BadRequest(new { message = "The private chat could not be created." });
            var mapped = MapChat(chat, null);
            await _hub.Clients.Users(new[] { currentUserId.ToString(), otherUser.Id.ToString() }).SendAsync("ChatCreated", mapped);
            return Ok(new { message = "Private chat created successfully.", chat = mapped });
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to create private chat."); return Problem(statusCode: 500, title: "Private chat creation failed"); }
    }

    [HttpPost("group")]
    public async Task<IActionResult> CreateGroup(CreateGroupChatDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Group name is required." });
        try
        {
            var requestedUsernames = (dto.Usernames ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var eligibleUsernames = new List<string>();
            var skippedUsernames = new List<string>();

            foreach (var username in requestedUsernames)
            {
                var user = await _chatService.GetUserByUsernameAsync(username);
                if (user == null) return BadRequest(new { message = $"User @{username} was not found." });

                if (user.Id == userId || user.AllowGroupAdds)
                    eligibleUsernames.Add(user.Username);
                else
                    skippedUsernames.Add(user.Username);
            }

            // An empty eligible list is valid: the service will create the group
            // for its creator and simply omit all protected selected users.
            var chat = await _chatService.CreateGroupChatAsync(userId, dto.Name, eligibleUsernames);
            if (chat == null)
                return BadRequest(new { message = "The group could not be created. Check the group name and your account." });

            var mapped = MapChat(chat, null);
            await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ChatCreated", mapped);

            var message = skippedUsernames.Count == 0
                ? "Group created successfully."
                : $"Group created successfully. The following users were not added because they do not allow group additions: {string.Join(", ", skippedUsernames.Select(x => "@" + x))}.";

            return Ok(new { message, chat = mapped, skippedUsernames });
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to create group chat."); return Problem(statusCode: 500, title: "Group creation failed"); }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChats()
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return Ok((await _chatService.GetUserChatsAsync(userId)).Select(c => MapChat(c, c.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("{chatId}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        return Ok((await _chatService.GetMembersAsync(chatId)).Select(MapMember).ToList());
    }

    [HttpPost("{chatId}/members")]
    public async Task<IActionResult> AddMember(int chatId, AddGroupMemberDto dto)
    {
        if (!TryGetCurrentUserId(out var actorId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var user = await _chatService.GetUserByUsernameAsync(dto.Username);
        if (user == null) return NotFound(new { message = "User not found." });
        if (!await _chatService.AddMemberAsync(chatId, actorId, user.Id))
        {
            if (!user.AllowGroupAdds)
                return BadRequest(new { message = $"@{user.Username} does not allow other people to add them to groups." });
            return Forbid();
        }
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound();
        var mapped = MapChat(chat, null);
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("GroupUpdated", mapped);
        return Ok(new { message = "Member added successfully.", chat = mapped });
    }

    [HttpDelete("{chatId}/members/{userId:long}")]
    public async Task<IActionResult> RemoveMember(int chatId, long userId)
    {
        if (!TryGetCurrentUserId(out var actorId)) return Unauthorized();
        if (!await _chatService.RemoveMemberAsync(chatId, actorId, userId)) return Forbid();
        await _hub.Clients.User(userId.ToString()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });
        return Ok(new { message = "Member removed successfully." });
    }

    [HttpPost("{chatId}/leave")]
    public async Task<IActionResult> LeaveGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await _chatService.LeaveGroupAsync(chatId, userId)) return BadRequest(new { message = "Only non-owner members can leave a group." });
        await _hub.Clients.User(userId.ToString()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });
        return Ok(new { message = "You left the group." });
    }

    [HttpPut("{chatId}/name")]
    public async Task<IActionResult> RenameGroup(int chatId, RenameGroupDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Group name is required." });
        if (!await _chatService.RenameGroupAsync(chatId, userId, dto.Name)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound();
        var mapped = MapChat(chat, null);
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("GroupUpdated", mapped);
        return Ok(mapped);
    }

    [HttpPost("{chatId}/avatar"), RequestSizeLimit(MaxGroupAvatarBytes)]
    public async Task<IActionResult> UploadGroupAvatar(int chatId, IFormFile file)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || chat.Type != ChatType.Group) return NotFound(new { message = "Group not found." });
        var member = await _chatService.GetMemberAsync(chatId, userId);
        if (member == null || member.Role == (int)ChatMemberRole.Member) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { message = "Please select an image." });
        if (file.Length > MaxGroupAvatarBytes) return BadRequest(new { message = "Group picture must be 5 MB or smaller." });
        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return BadRequest(new { message = "Only JPG, PNG and WebP images are supported." });
        try
        {
            await using var input = file.OpenReadStream();
            using var image = await Image.LoadAsync(input);
            if (image.Width < 64 || image.Height < 64) return BadRequest(new { message = "Image must be at least 64x64 pixels." });
            var dir = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "groups");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid():N}.jpg";
            var fullPath = Path.Combine(dir, fileName);
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(512, 512), Mode = ResizeMode.Crop }));
            await image.SaveAsJpegAsync(fullPath, new JpegEncoder { Quality = 88 });
            var old = chat.AvatarUrl;
            chat.AvatarUrl = $"/uploads/groups/{fileName}";
            await _chatService.SaveChangesAsync();
            DeleteStoredGroupAvatar(old);
            var refreshed = await _chatService.GetChatByIdAsync(chatId);
            if (refreshed == null) return NotFound();
            var mapped = MapChat(refreshed, null);
            await _hub.Clients.Users(RecipientIds(refreshed)).SendAsync("GroupUpdated", mapped);
            return Ok(new { message = "Group picture updated successfully.", chat = mapped });
        }
        catch (UnknownImageFormatException) { return BadRequest(new { message = "The uploaded file is not a valid image." }); }
    }

    [HttpDelete("{chatId}/avatar")]
    public async Task<IActionResult> DeleteGroupAvatar(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || chat.Type != ChatType.Group) return NotFound(new { message = "Group not found." });
        var member = await _chatService.GetMemberAsync(chatId, userId);
        if (member == null || member.Role == (int)ChatMemberRole.Member) return Forbid();
        var old = chat.AvatarUrl;
        chat.AvatarUrl = null;
        await _chatService.SaveChangesAsync();
        DeleteStoredGroupAvatar(old);
        var refreshed = await _chatService.GetChatByIdAsync(chatId);
        var mapped = MapChat(refreshed!, null);
        await _hub.Clients.Users(RecipientIds(refreshed!)).SendAsync("GroupUpdated", mapped);
        return Ok(new { message = "Group picture removed successfully." });
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats() => Ok((await _chatService.GetAllChatsAsync()).Select(c => MapChat(c, c.Messages.FirstOrDefault())).ToList());

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        pageSize = Math.Clamp(pageSize, 1, 100);
        var messages = await _chatService.GetMessagesAsync(chatId, userId, beforeMessageId, pageSize);
        var first = messages.FirstOrDefault();
        return Ok(new ChatHistoryResponseDto
        {
            Messages = messages.Select(m => MessageDtoMapper.Map(m)).ToList(),
            HasMore = first != null && await _chatService.HasOlderMessagesAsync(chatId, userId, first.Id),
            NextBeforeMessageId = first?.Id
        });
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> SendMessage(int chatId, SendMessageDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        var message = await _chatService.SendMessageAsync(chatId, userId, dto.Content);
        if (message == null) return BadRequest(new { message = "Unable to send message." });
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat != null) await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
        return Ok(new { message = "Message sent successfully.", data = MessageDtoMapper.Map(message) });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!IsOwner() && !await _chatService.CanAccessChatAsync(chatId, userId)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });
        if (!await _chatService.DeleteChatAsync(chatId)) return NotFound();
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ChatDeleted", new { chatId = chat.Id, deletedBy = userId.ToString() });
        return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null) return NotFound(new { message = "Message not found." });
        var currentUsername = User.FindFirst("username")?.Value;
        if (!IsOwner() && (!await _chatService.CanAccessChatAsync(message.ChatId, userId) || !string.Equals(message.SenderId, currentUsername, StringComparison.OrdinalIgnoreCase))) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) return NotFound();
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId, content = message.Content, sentAt = message.SentAt });
        return Ok(new { message = "Message deleted successfully." });
    }

    private ChatListDto MapChat(Chat chat, Message? lastMessage) => new()
    {
        Id = chat.Id,
        Type = chat.Type,
        Name = chat.Type == ChatType.Group ? chat.Name : string.Empty,
        AvatarUrl = chat.Type == ChatType.Group ? ToAbsoluteChatAvatarUrl(chat.AvatarUrl) : null,
        CreatedByUserId = chat.CreatedByUserId?.ToString() ?? string.Empty,
        User1Id = chat.User1Id?.ToString() ?? string.Empty,
        User2Id = chat.User2Id?.ToString() ?? string.Empty,
        User1Name = chat.User1?.DisplayName ?? string.Empty,
        User2Name = chat.User2?.DisplayName ?? string.Empty,
        User1AvatarUrl = ToAbsoluteAvatarUrl(chat.User1?.AvatarUrl),
        User2AvatarUrl = ToAbsoluteAvatarUrl(chat.User2?.AvatarUrl),
        CreatedAt = chat.CreatedAt,
        LastMessage = lastMessage == null ? null : MessageDtoMapper.Map(lastMessage)
    };

    private GroupMemberDto MapMember(ChatMember m) => new()
    {
        UserId = m.UserId.ToString(),
        Username = m.User.Username,
        DisplayName = m.User.DisplayName,
        AvatarUrl = ToAbsoluteAvatarUrl(m.User.AvatarUrl),
        Role = ((ChatMemberRole)m.Role).ToString(),
        JoinedAt = m.JoinedAt
    };

    private IEnumerable<string> RecipientIds(Chat chat)
    {
        var recipients = new List<string>();
        if (chat.Type == ChatType.Group)
        {
            foreach (var member in chat.ChatMembers)
            {
                var id = member.UserId.ToString();
                if (!string.IsNullOrWhiteSpace(id) && !recipients.Contains(id)) recipients.Add(id);
            }
        }
        else
        {
            if (chat.User1Id.HasValue && chat.User1Id.Value > 0) recipients.Add(chat.User1Id.Value.ToString());
            if (chat.User2Id.HasValue && chat.User2Id.Value > 0)
            {
                var id = chat.User2Id.Value.ToString();
                if (!recipients.Contains(id)) recipients.Add(id);
            }
        }
        return recipients;
    }

    private string? ToAbsoluteAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out _)) return avatarUrl;
        return $"{Request.Scheme}://{Request.Host}{(avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl)}";
    }

    private string? ToAbsoluteChatAvatarUrl(string? avatarUrl) => ToAbsoluteAvatarUrl(avatarUrl);

    private void DeleteStoredGroupAvatar(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;
        var fileName = Path.GetFileName(avatarUrl);
        if (string.IsNullOrWhiteSpace(fileName)) return;
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "groups", fileName);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }

    private bool TryGetCurrentUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private bool IsOwner()
    {
        var ownerUsername = _configuration["Owner:Username"];
        var username = User.FindFirst("username")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerUsername) && string.Equals(ownerUsername, username, StringComparison.OrdinalIgnoreCase)) return true;
        var ownerUserId = _configuration["Owner:UserId"];
        return !string.IsNullOrWhiteSpace(ownerUserId) && ownerUserId == User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private Task<bool> CanAccessChat(int chatId, long userId) => _chatService.CanAccessChatAsync(chatId, userId);
}
