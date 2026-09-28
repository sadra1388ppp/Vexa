namespace NovaChat.Server.Entities;

// Pomelo owns Entities/Generated. These constructors preserve application defaults
// when those files are regenerated. Never add constructors to generated files.
public partial class User
{
    public User()
    {
        Username = DisplayName = Email = PasswordHash = Bio = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }
}

public partial class Chat
{
    public Chat()
    {
        Type = ChatType.Private;
        Name = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }
}

public partial class ChatMember
{
    public ChatMember()
    {
        Role = (int)ChatMemberRole.Member;
        JoinedAt = DateTime.UtcNow;
    }
}

public partial class Message
{
    public Message()
    {
        Content = DeletedForUserIds = string.Empty;
        SentAt = DateTime.UtcNow;
    }
}

public partial class Contact
{
    public Contact() => CreatedAt = DateTime.UtcNow;
}
