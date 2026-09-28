using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public sealed class DatabaseInitializer(AppDbContext context, MessageReadService messageReadService, E2eeDeviceService e2eeDeviceService, ChatRequestService chatRequestService)
{
    public async Task<bool> InitializeEmptyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var created = await context.Database.EnsureCreatedAsync(cancellationToken);
        await messageReadService.EnsureSchemaAsync(cancellationToken);
        await e2eeDeviceService.EnsureSchemaAsync(cancellationToken);
        await chatRequestService.EnsureSchemaAsync(cancellationToken);
        await ValidateSchemaAsync(cancellationToken);
        return created;
    }

    private async Task EnsureMessageEditSchemaAsync(CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Messages ADD COLUMN IF NOT EXISTS EditedAt DATETIME(6) NULL;",
            cancellationToken);
    }

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await chatRequestService.EnsureSchemaAsync(cancellationToken);
            await context.Users.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Chats.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await EnsureMessageEditSchemaAsync(cancellationToken);
            await context.Messages.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Contacts.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await messageReadService.EnsureSchemaAsync(cancellationToken);
            await e2eeDeviceService.EnsureSchemaAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "NovaChat could not read its MariaDB schema. Check the connection and permissions. " +
                "For a new database use --initialize-database; for an existing database follow docs/MARIADB.md. " +
                "Startup only adds the MessageReads, EncryptionDevices and ChatRequests schema when missing and never imports existing data.", exception);
        }
    }
}
