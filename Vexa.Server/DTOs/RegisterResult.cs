using NovaChat.Server.DTOs;

namespace NovaChat.Server.DTOs;

public class RegisterResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public UserResponseDto? User { get; set; }
}
