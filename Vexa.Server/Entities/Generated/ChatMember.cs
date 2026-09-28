using System;
using System.Collections.Generic;

namespace NovaChat.Server.Entities;

public partial class ChatMember
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public long UserId { get; set; }

    public int Role { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
