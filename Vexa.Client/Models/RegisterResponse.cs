namespace NovaChat.Client.Models;

public class RegisterResponse
{
    public string Message { get; set; } = string.Empty;

    public RegisterUser User { get; set; } = new();
}

public class RegisterUser
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}