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
[Route("api/message-deletion")]
[Authorize]
public class MessageDeletionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ChatService _chatService;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hub;

    public MessageDeletionController(
        AppDbContext db,
        ChatService chatService,
        IConfiguration configuration,
        IHubContext<ChatHub> hub)
    {
        _db = db;
        _chatService = chatService;
        _configuration = configuration;
        _hub = hub;
    }

    [HttpDelete("{messageId:int}")]
    public Task<IActionResult> Delete(int messageId, DeleteMessageDto dto)
        => DeleteCoreAsync(messageId, dto);

    [HttpDelete("private/{messageId:int}")]
    public Task<IActionResult> DeletePrivate(int messageId, DeleteMessageDto dto)
        => DeleteCoreAsync(messageId, dto);

    private async Task<IActionResult> DeleteCoreAsync(int messageId, DeleteMessageDto? dto)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var message = await _db.Messages
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
            return NotFound(new { message = "Message not found." });

        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null)
            return NotFound(new { message = "Chat not found." });

        var owner = IsOwner(userId);
        var isMember = chat.ChatMembers.Any(m => m.UserId == userId);

        if (!owner && !isMember)
            return Forbid();

        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "me";
        if (mode is not ("me" or "everyone"))
            return BadRequest(new { message = "Mode must be 'me' or 'everyone'." });

        if (mode == "everyone")
        {
            var currentUsername = User.FindFirst("username")?.Value;
            if (!owner && !string.Equals(message.SenderId, currentUsername, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            // SECURITY RULE: logical deletion only.
            // Never remove the Message row, clear Content, or delete stored media.
            // The original message remains intact in MariaDB for audit/history.
            message.DeletedForEveryone = true;
            await _db.SaveChangesAsync();

            var deletedPayload = new
            {
                id = message.Id,
                chatId = message.ChatId,
                senderId = message.SenderId,
                sentAt = message.SentAt
            };

            var recipients = GetRecipientIds(chat);
            if (recipients.Count > 0)
            {
                await _hub.Clients
                    .Users(recipients)
                    .SendAsync("MessageDeleted", deletedPayload);
            }
        }
        else
        {
            AddDeletedForUser(message, userId.ToString());
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Message deleted successfully.", mode });
    }

    private List<string> GetRecipientIds(Chat chat)
    {
        return chat.ChatMembers
            .Select(m => m.UserId.ToString())
            .Distinct()
            .ToList();
    }

    private bool IsOwner(long userId)
    {
        var ownerUsername = _configuration["Owner:Username"];
        var currentUsername = User.FindFirst("username")?.Value;

        if (!string.IsNullOrWhiteSpace(ownerUsername) &&
            string.Equals(ownerUsername, currentUsername, StringComparison.OrdinalIgnoreCase))
            return true;

        return long.TryParse(_configuration["Owner:UserId"], out var configuredOwnerId) &&
               configuredOwnerId == userId;
    }

    private static void AddDeletedForUser(Message message, string userId)
    {
        var values = ParseDeletedForUsers(message.DeletedForUserIds);
        if (!values.Contains(userId, StringComparer.Ordinal))
            values.Add(userId);
        message.DeletedForUserIds = string.Join('|', values);
    }

    private static List<string> ParseDeletedForUsers(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private bool TryGetCurrentUserId(out long userId)
        => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
