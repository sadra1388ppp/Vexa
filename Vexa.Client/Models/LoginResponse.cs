namespace NovaChat.Client.Models;

public class LoginResponse
{
    public string Message { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public LoginUser User { get; set; } = new();
}

public class LoginUser
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}