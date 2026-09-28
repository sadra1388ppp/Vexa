using System.Text.Json.Serialization;

namespace NovaChat.Client.Models;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string Id
    {
        get => Username;
        set => Username = value;
    }

    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MessagePrivacy { get; set; } = "Everybody";
    public bool AllowGroupAdds { get; set; } = true;
}