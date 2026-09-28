using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public partial class AppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.MessagePrivacy)
                .HasMaxLength(32)
                .HasDefaultValue("Everybody");

            // The database stores the value as the literal text "true" or "false",
            // while the generated entity keeps the application-facing bool type.
            entity.Property(e => e.AllowGroupAdds)
                .HasColumnType("varchar(5)")
                .HasMaxLength(5)
                .HasConversion(
                    value => value ? "true" : "false",
                    value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                .HasDefaultValue("true");
        });
    }
}
