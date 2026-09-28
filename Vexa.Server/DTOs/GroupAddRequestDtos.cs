namespace NovaChat.Server.DTOs;

public sealed class CreateGroupAddRequestDto
{
    public string Username { get; set; } = string.Empty;
}

public sealed class GroupAddRequestDto
{
    public long Id { get; set; }
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string RequesterUserId { get; set; } = string.Empty;
    public string RequesterUsername { get; set; } = string.Empty;
    public string RequesterDisplayName { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string TargetUsername { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}