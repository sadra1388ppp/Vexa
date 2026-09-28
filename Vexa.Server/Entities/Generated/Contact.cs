using System;
using System.Collections.Generic;

namespace NovaChat.Server.Entities;

public partial class Contact
{
    public int Id { get; set; }

    public long OwnerUserId { get; set; }

    public long ContactUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User ContactUser { get; set; } = null!;

    public virtual User OwnerUser { get; set; } = null!;
}
