using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace NovaChat.Server.Entities;

public partial class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    // Human-readable username of the sender, stored directly in the message row.
    public string SenderId { get; set; } = string.Empty;

    public string Content { get; set; } = null!;

    public DateTime SentAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool DeletedForEveryone { get; set; }

    public string DeletedForUserIds { get; set; } = null!;

    public virtual Chat Chat { get; set; } = null!;

    // Kept only as a compatibility property for older client/server code; it is not persisted.
    [NotMapped]
    public virtual User? Sender { get; set; }
}
