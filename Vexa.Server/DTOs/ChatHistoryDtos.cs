using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.DTOs;

public class MessageDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsEdited => EditedAt.HasValue;
    public bool IsDeletedForEveryone { get; set; }
    public string MessageType { get; set; } = "text";
    public string? AttachmentUrl { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }
    public double? DurationSeconds { get; set; }
}

public class ChatHistoryResponseDto
{
    public List<MessageDto> Messages { get; set; } = [];
    public bool HasMore { get; set; }
    public int? NextBeforeMessageId { get; set; }
}

public class ChatListDto
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
    public MessageDto? LastMessage { get; set; }
}

public static class MessageDtoMapper
{
    public static MessageDto Map(Message message, string? ignoredBaseUrl = null)
    {
        var dto = new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = message.SenderId,
            Content = message.Content,
            SentAt = message.SentAt,
            EditedAt = message.EditedAt,
            IsDeletedForEveryone = message.DeletedForEveryone
        };

        if (E2eeMediaMessageEnvelope.TryParse(message.Content, out _))
        {
            dto.MessageType = "e2ee-media";
            dto.AttachmentUrl = $"/api/ChatMedia/{message.Id}";
            return dto;
        }

        if (MediaMessageEnvelope.TryParse(message.Content, out var media) && media != null)
        {
            dto.MessageType = media.Type;
            dto.FileName = media.FileName;
            dto.ContentType = media.ContentType;
            dto.FileSize = media.Size;
            dto.DurationSeconds = media.DurationSeconds;
            dto.AttachmentUrl = $"/api/ChatMedia/{message.Id}";
            var icon = media.Type switch { "image" => "📷", "voice" => "🎙", _ => "📎" };
            dto.Content = $"{icon} {media.FileName}\u200B{message.Id}";
        }

        return dto;
    }
}