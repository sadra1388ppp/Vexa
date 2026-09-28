using System;
using System.Collections.Generic;

namespace NovaChat.Server.Entities;

public partial class User
{
    public long Id { get; set; }
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = null!;
    public string Bio { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string MessagePrivacy { get; set; } = "Everybody";
    public bool AllowGroupAdds { get; set; } = true;

    public virtual ICollection<Chat> ChatCreatedByUsers { get; set; } = new List<Chat>();
    public virtual ICollection<Contact> ContactContactUsers { get; set; } = new List<Contact>();
    public virtual ICollection<Contact> ContactOwnerUsers { get; set; } = new List<Contact>();
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
