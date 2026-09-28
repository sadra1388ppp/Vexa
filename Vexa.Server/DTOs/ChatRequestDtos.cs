namespace NovaChat.Server.DTOs;

public sealed class CreateChatRequestDto
{
    public string Username { get; set; } = string.Empty;
}

public sealed class ChatRequestDto
{
    public long Id { get; set; }
    public string RequesterUserId { get; set; } = string.Empty;
    public string RequesterUsername { get; set; } = string.Empty;
    public string RequesterDisplayName { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string TargetUsername { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public int? ChatId { get; set; }
}