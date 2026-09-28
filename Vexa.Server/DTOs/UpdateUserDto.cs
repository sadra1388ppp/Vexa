using System.ComponentModel.DataAnnotations;

namespace NovaChat.Server.DTOs;

public class UpdateUserDto
{
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

    [StringLength(160)]
    public string? Bio { get; set; }

    [StringLength(32, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Username may contain only letters, numbers, dot, underscore and hyphen.")]
    public string? NewUsername { get; set; }
}