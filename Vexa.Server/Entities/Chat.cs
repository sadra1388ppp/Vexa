using System.ComponentModel.DataAnnotations.Schema;

namespace NovaChat.Server.Entities;

public static class ChatType
{
    public const string Private = "Private Chat";
    public const string Group = "Group";
}

public partial class Chat
{
    public int Id { get; set; }
    public string Type { get; set; } = ChatType.Private;
    public string Name { get; set; } = string.Empty;
    public string Members { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public long? CreatedByUserId { get; set; }
    public virtual User? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    // Compatibility-only in-memory collection. It is not persisted as a database table.
    [NotMapped]
    public ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    [NotMapped]
    public long? User1Id { get; set; }

    [NotMapped]
    public long? User2Id { get; set; }

    [NotMapped]
    public User? User1 { get; set; }

    [NotMapped]
    public User? User2 { get; set; }
}
