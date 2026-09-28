using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public virtual DbSet<Chat> Chats { get; set; }
    public virtual DbSet<Contact> Contacts { get; set; }
    public virtual DbSet<Message> Messages { get; set; }
    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_unicode_ci").HasCharSet("utf8mb4");

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => e.CreatedByUserId, "IX_Chats_CreatedByUserId");
            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Type).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(128);
            entity.Property(e => e.Members).HasMaxLength(4000);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.CreatedByUserId).HasColumnType("bigint(20)");
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Property(e => e.IsDeleted).HasColumnType("tinyint(1)");
            entity.Property(e => e.DeletedAt).HasMaxLength(6);
            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ChatCreatedByUsers).HasForeignKey(d => d.CreatedByUserId);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => e.ContactUserId, "IX_Contacts_ContactUserId");
            entity.HasIndex(e => new { e.OwnerUserId, e.ContactUserId }, "IX_Contacts_OwnerUserId_ContactUserId").IsUnique();
            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ContactUserId).HasColumnType("bigint(20)");
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Property(e => e.OwnerUserId).HasColumnType("bigint(20)");
            entity.HasOne(d => d.ContactUser).WithMany(p => p.ContactContactUsers).HasForeignKey(d => d.ContactUserId);
            entity.HasOne(d => d.OwnerUser).WithMany(p => p.ContactOwnerUsers).HasForeignKey(d => d.OwnerUserId);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => new { e.ChatId, e.SentAt, e.Id }, "IX_Messages_ChatId_SentAt_Id");
            entity.HasIndex(e => e.SenderId, "IX_Messages_SenderId");
            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ChatId).HasColumnType("int(11)");
            entity.Property(e => e.SenderId).HasMaxLength(32).IsRequired();
            entity.Property(e => e.SentAt).HasMaxLength(6);
            entity.Property(e => e.EditedAt).HasMaxLength(6);
            entity.HasOne(d => d.Chat).WithMany(p => p.Messages).HasForeignKey(d => d.ChatId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => e.Email, "IX_Users_Email").IsUnique();
            entity.HasIndex(e => e.PhoneNumber, "IX_Users_PhoneNumber").IsUnique();
            entity.HasIndex(e => e.Username, "IX_Users_Username").IsUnique();
            entity.Property(e => e.Id).HasColumnType("bigint(20)");
            entity.Property(e => e.Username).HasMaxLength(32);
            entity.Property(e => e.DisplayName).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.PhoneNumber).HasMaxLength(32);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.Bio).HasMaxLength(160);
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.LastSeenAt).HasMaxLength(6);
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Ignore(e => e.Messages);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
