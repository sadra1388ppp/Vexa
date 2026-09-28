using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using System.Data;

namespace NovaChat.Server.Services;

public sealed record E2eeDeviceRecord(string DeviceId, long UserId, string PublicKeyPem, DateTime CreatedAt, DateTime LastSeenAt);

public sealed class E2eeDeviceService(AppDbContext db, IConfiguration configuration)
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _configuration = configuration;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS EncryptionDevices (
    DeviceId VARCHAR(64) NOT NULL,
    UserId BIGINT NOT NULL,
    PublicKeyPem TEXT NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    LastSeenAt DATETIME(6) NOT NULL,
    RevokedAt DATETIME(6) NULL,
    PRIMARY KEY (DeviceId),
    INDEX IX_EncryptionDevices_UserId (UserId),
    CONSTRAINT FK_EncryptionDevices_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", cancellationToken);
    }

    public async Task UpsertAsync(long userId, string deviceId, string publicKeyPem, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var now = IranTime.Now;
        const string sql = @"
INSERT INTO EncryptionDevices (DeviceId, UserId, PublicKeyPem, CreatedAt, LastSeenAt, RevokedAt)
VALUES ({0}, {1}, {2}, {3}, {3}, NULL)
ON DUPLICATE KEY UPDATE
    UserId = VALUES(UserId),
    PublicKeyPem = VALUES(PublicKeyPem),
    LastSeenAt = VALUES(LastSeenAt),
    RevokedAt = NULL;";
        await _db.Database.ExecuteSqlRawAsync(sql, [deviceId, userId, publicKeyPem, now], cancellationToken);
    }

    public async Task<List<E2eeDeviceRecord>> GetChatDevicesAsync(int chatId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var usernames = await _db.Chats.AsNoTracking()
            .Where(c => c.Id == chatId && !c.IsDeleted)
            .Select(c => c.Members)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(usernames)) return [];

        var names = usernames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return [];

        var memberIds = await _db.Users.AsNoTracking()
            .Where(u => names.Contains(u.Username))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (!memberIds.Contains(userId)) return [];
        var ownerUsername = _configuration["Owner:Username"]?.Trim();
        if (!string.IsNullOrWhiteSpace(ownerUsername))
        {
            var ownerId = await _db.Users.AsNoTracking()
                .Where(u => u.Username == ownerUsername)
                .Select(u => (long?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (ownerId.HasValue)
                memberIds.Add(ownerId.Value);
        }

        var recipientUserIds = memberIds.Distinct().ToList();
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, UserId, PublicKeyPem, CreatedAt, LastSeenAt FROM EncryptionDevices WHERE RevokedAt IS NULL AND UserId IN (" + string.Join(',', recipientUserIds.Select((_, i) => "@p" + i)) + ")";
        for (var i = 0; i < recipientUserIds.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i;
            parameter.Value = recipientUserIds[i];
            command.Parameters.Add(parameter);
        }

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<E2eeDeviceRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new E2eeDeviceRecord(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetDateTime(4)));
        }

        return result
            .GroupBy(x => x.DeviceId, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
    }

    public async Task<E2eeDeviceRecord?> GetDeviceAsync(string deviceId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, UserId, PublicKeyPem, CreatedAt, LastSeenAt FROM EncryptionDevices WHERE DeviceId = @deviceId AND UserId = @userId AND RevokedAt IS NULL LIMIT 1";

        var deviceParameter = command.CreateParameter();
        deviceParameter.ParameterName = "@deviceId";
        deviceParameter.Value = deviceId;
        command.Parameters.Add(deviceParameter);

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "@userId";
        userParameter.Value = userId;
        command.Parameters.Add(userParameter);

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new E2eeDeviceRecord(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetDateTime(4));
    }

    public async Task RevokeAsync(long userId, string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var now = IranTime.Now;
        const string sql = "UPDATE EncryptionDevices SET RevokedAt = {2} WHERE DeviceId = {0} AND UserId = {1};";
        await _db.Database.ExecuteSqlRawAsync(sql, [deviceId, userId, now], cancellationToken);
    }
}
