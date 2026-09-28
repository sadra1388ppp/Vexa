namespace NovaChat.Server.DTOs;

public class CreateGroupChatDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Usernames { get; set; } = [];
}

public class AddGroupMemberDto
{
    public string Username { get; set; } = string.Empty;
}

public class RenameGroupDto
{
    public string Name { get; set; } = string.Empty;
}

public class GroupMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "Member";
    public DateTime JoinedAt { get; set; }
}
