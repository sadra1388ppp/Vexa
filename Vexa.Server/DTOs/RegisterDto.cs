using System.ComponentModel.DataAnnotations;

namespace NovaChat.Server.DTOs;

public class RegisterDto
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Username may contain only letters, numbers, dot, underscore and hyphen.")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [StringLength(20)]
    public string MessagePrivacy { get; set; } = "Everybody";

    public bool AllowGroupAdds { get; set; } = true;
}