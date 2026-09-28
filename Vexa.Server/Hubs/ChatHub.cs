using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;
using System.Security.Claims;
using System.Text.Json;

namespace NovaChat.Server.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly PresenceService _presenceService;
    private readonly UserService _userService;
    private readonly MessageReadService _messageReadService;
    private readonly IConfiguration _configuration;
    private readonly E2eeDeviceService _e2eeDevices;

    public ChatHub(ChatService chatService, PresenceService presenceService, UserService userService, MessageReadService messageReadService, IConfiguration configuration, E2eeDeviceService e2eeDevices)
    {
        _chatService = chatService;
        _presenceService = presenceService;
        _userService = userService;
        _messageReadService = messageReadService;
        _configuration = configuration;
        _e2eeDevices = e2eeDevices;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) { Context.Abort(); return; }
        if (_presenceService.UserConnected(userId)) await Clients.All.SendAsync("UserOnline", userId);
        await Clients.Caller.SendAsync("PresenceSnapshot", _presenceService.GetOnlineUsers());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId();
        if (!string.IsNullOrWhiteSpace(userId) && _presenceService.UserDisconnected(userId))
        {
            await _userService.MarkLastSeenAsync(userId);
            await Clients.All.SendAsync("UserOffline", userId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task<List<string>> GetOnlineUsers() => Task.FromResult(_presenceService.GetOnlineUsers().ToList());
    public Task<bool> IsUserOnline(string userId) => Task.FromResult(!string.IsNullOrWhiteSpace(userId) && _presenceService.IsOnline(userId));

    public async Task SendMessage(int chatId, string content)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        if (!IsE2eeEnvelope(content)) throw new HubException("NovaChat requires end-to-end encrypted messages.");
        if (content.Length > 250_000) throw new HubException("Encrypted message is too large.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId)) throw new HubException("You do not have access to this chat.");

        var message = await _chatService.SendMessageAsync(chatId, userId, content);
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (message == null || chat == null) throw new HubException("Unable to send message.");

        if (chat.Type == ChatType.Group)
            await Clients.Group($"chat-{chatId}").SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
        else
            await Clients.Users(Recipients(chat)).SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
    }

    public async Task EditMessage(int messageId, string encryptedContent)
    {
        if (!TryGetCurrentUserId(out var userId))
            throw new HubException("Unauthorized.");

        if (!IsE2eeEnvelope(encryptedContent))
            throw new HubException("NovaChat requires end-to-end encrypted messages.");

        if (encryptedContent.Length > 250_000)
            throw new HubException("Encrypted message is too large.");

        var message = await _chatService.EditMessageAsync(messageId, userId, encryptedContent);
        if (message == null)
            throw new HubException("You can only edit your own active messages.");

        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null)
            throw new HubException("Chat not found.");

        await Clients.Users(Recipients(chat))
            .SendAsync("MessageEdited", MessageDtoMapper.Map(message));
    }

    public async Task RequestMessageKey(int messageId, string requesterDeviceId)
    {
        if (!TryGetCurrentUserId(out var requesterUserId))
            throw new HubException("Unauthorized.");

        if (messageId <= 0 || string.IsNullOrWhiteSpace(requesterDeviceId))
            throw new HubException("Invalid key recovery request.");

        var targetDevice = await _e2eeDevices.GetDeviceAsync(requesterDeviceId.Trim(), requesterUserId);
        if (targetDevice == null)
            throw new HubException("The requested encryption device is not registered.");

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || message.DeletedForEveryone)
            throw new HubException("Message not found.");

        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null)
            throw new HubException("Chat not found.");

        var requesterIsMember = chat.ChatMembers.Any(m => m.UserId == requesterUserId);
        if (!requesterIsMember)
            throw new HubException("You do not have access to this chat.");

        var requesterUsername = Context.User?.FindFirst("username")?.Value ?? string.Empty;
        var senderIds = chat.ChatMembers
            .Select(m => m.UserId.ToString())
            .Where(id => !string.Equals(id, requesterUserId.ToString(), StringComparison.Ordinal))
            .Distinct()
            .ToList();

        if (senderIds.Count == 0)
            return;

        await Clients.Users(senderIds).SendAsync("MessageKeyRequested", new
        {
            messageId = message.Id,
            chatId = message.ChatId,
            requesterUserId = requesterUserId.ToString(),
            requesterDeviceId = targetDevice.DeviceId,
            requesterPublicKeyPem = targetDevice.PublicKeyPem,
            requesterUsername
        });
    }

    public async Task DeliverMessageKey(int messageId, int chatId, string targetUserId, string targetDeviceId, string wrappedKey)
    {
        if (!TryGetCurrentUserId(out var senderUserId))
            throw new HubException("Unauthorized.");

        if (messageId <= 0 || chatId <= 0 || string.IsNullOrWhiteSpace(targetUserId) || string.IsNullOrWhiteSpace(targetDeviceId) || string.IsNullOrWhiteSpace(wrappedKey))
            throw new HubException("Invalid key delivery.");

        if (!long.TryParse(targetUserId, out var receiverUserId) || receiverUserId <= 0 || receiverUserId == senderUserId)
            throw new HubException("Invalid recipient.");

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || message.ChatId != chatId || message.DeletedForEveryone)
            throw new HubException("Message not found.");

        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || !chat.ChatMembers.Any(m => m.UserId == senderUserId) || !chat.ChatMembers.Any(m => m.UserId == receiverUserId))
            throw new HubException("You do not have access to this chat.");

        var targetDevice = await _e2eeDevices.GetDeviceAsync(targetDeviceId.Trim(), receiverUserId);
        if (targetDevice == null)
            throw new HubException("The target encryption device is not registered.");

        if (wrappedKey.Length > 2000)
            throw new HubException("Wrapped message key is too large.");

        await Clients.User(receiverUserId.ToString()).SendAsync("MessageKeyDelivered", new
        {
            messageId,
            chatId,
            deviceId = targetDevice.DeviceId,
            wrappedKey
        });
    }

    public async Task MarkChatAsRead(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId)) throw new HubException("You do not have access to this chat.");
        var messageIds = await _messageReadService.MarkChatAsReadAsync(chatId, userId);
        if (messageIds.Count == 0) return;
        await Clients.Group($"chat-{chatId}").SendAsync("MessagesRead", new { ChatId = chatId, ReaderUserId = userId.ToString(), MessageIds = messageIds });
    }

    public async Task DeleteMessage(int messageId)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null) throw new HubException("Message not found.");
        var username = Context.User?.FindFirst("username")?.Value;
        if (!IsOwner() && (!string.Equals(message.SenderId, username, StringComparison.OrdinalIgnoreCase) || !await _chatService.CanAccessChatAsync(message.ChatId, userId))) throw new HubException("You do not have permission to delete this message.");
        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) throw new HubException("Unable to send deletion.");
        await Clients.Users(Recipients(chat)).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId, content = message.Content, sentAt = message.SentAt });
    }

    public async Task JoinChat(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId)) throw new HubException("You do not have access to this chat.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{chatId}");
    }

    public Task LeaveChat(int chatId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{chatId}");

    private IEnumerable<string> Recipients(Chat chat) => chat.ChatMembers.Select(m => m.UserId.ToString()).Distinct();

    private string? CurrentUserId() => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool TryGetCurrentUserId(out long userId) => long.TryParse(CurrentUserId(), out userId) && userId > 0;

    private bool IsOwner()
    {
        var ownerUsername = _configuration["Owner:Username"];
        var username = Context.User?.FindFirst("username")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerUsername) && string.Equals(ownerUsername, username, StringComparison.OrdinalIgnoreCase)) return true;
        return long.TryParse(_configuration["Owner:UserId"], out var ownerId) && long.TryParse(CurrentUserId(), out var currentId) && ownerId == currentId;
    }

    private static bool IsE2eeEnvelope(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("v", out var version) || version.GetInt32() != 1) return false;
            if (!root.TryGetProperty("alg", out var alg) || alg.GetString() != "AES-256-GCM+RSA-OAEP-SHA256") return false;
            if (!root.TryGetProperty("nonce", out var nonce) || string.IsNullOrWhiteSpace(nonce.GetString())) return false;
            if (!root.TryGetProperty("tag", out var tag) || string.IsNullOrWhiteSpace(tag.GetString())) return false;
            if (!root.TryGetProperty("ciphertext", out var cipher) || string.IsNullOrWhiteSpace(cipher.GetString())) return false;
            if (!root.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Object || keys.EnumerateObject().Count() == 0) return false;
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
