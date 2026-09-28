namespace NovaChat.Server.Entities;

public sealed class MessageRead
{
    public int MessageId { get; set; }
    public long UserId { get; set; }
    public DateTime ReadAt { get; set; }
}
