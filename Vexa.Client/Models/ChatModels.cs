using NovaChat.Client.Services;

namespace NovaChat.Client.Models;

public class ChatModel
{
    public int Id { get; set; }
    public string Type { get; set; } = "Private Chat";
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string User1Id { get; set; } = string.Empty;
    public string User2Id { get; set; } = string.Empty;
    public string User1Name { get; set; } = string.Empty;
    public string User2Name { get; set; } = string.Empty;
    public string? User1AvatarUrl { get; set; }
    public string? User2AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public MessageModel? LastMessage { get; set; }
    public int UnreadCount { get; set; }
    public bool IsGroup => string.Equals(Type, "Group", StringComparison.OrdinalIgnoreCase) || string.Equals(Type, "1", StringComparison.OrdinalIgnoreCase);
    public string OtherUserId(string currentUserId) => IsGroup ? string.Empty : string.Equals(User1Id, currentUserId, StringComparison.OrdinalIgnoreCase) ? User2Id : User1Id;
    public string OtherUserName(string currentUserId) => IsGroup ? Name : string.Equals(User1Id, currentUserId, StringComparison.OrdinalIgnoreCase) ? User2Name : User1Name;
    public string? OtherUserAvatarUrl => IsGroup ? AvatarUrl : string.Equals(User1Id, AuthState.UserId, StringComparison.OrdinalIgnoreCase) ? User2AvatarUrl : User1AvatarUrl;
}

public class MessageModel
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeletedForEveryone { get; set; }
    public string MessageType { get; set; } = "text";
    public string? AttachmentUrl { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }
    public double? DurationSeconds { get; set; }
    public string DeliveryState { get; set; } = "sent";
}

public class ChatHistoryResponse { public List<MessageModel> Messages { get; set; } = []; public bool HasMore { get; set; } public int? NextBeforeMessageId { get; set; } }
public class CreateChatRequest { public string Username { get; set; } = string.Empty; }
public class CreateChatResponse { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; } public bool RequestPending { get; set; } public long? RequestId { get; set; } }
public class CreateGroupRequest { public string Name { get; set; } = string.Empty; public List<string> Usernames { get; set; } = []; }
public class CreateGroupResponse { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; }
}