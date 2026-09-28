namespace NovaChat.Server.DTOs;

public class AddContactDto
{
    public string UserId { get; set; } = string.Empty;
}

public class ContactResponseDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

public class UserSearchResultDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
