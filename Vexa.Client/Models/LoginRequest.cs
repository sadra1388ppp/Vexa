using System.Text.Json.Serialization;

namespace NovaChat.Client.Models;

public class LoginRequest
{
    public string Login { get; set; } = string.Empty;

    // Backward-compatible alias for the existing client code. It is not sent over the wire.
    [JsonIgnore]
    public string Id
    {
        get => Login;
        set => Login = value;
    }

    public string Password { get; set; } = string.Empty;
}
