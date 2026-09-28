using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using System.Data;

namespace NovaChat.Server.Services;

public sealed class MessageReadService(AppDbContext context)
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `MessageReads` (
    `MessageId` INT NOT NULL,
    `UserId` BIGINT NOT NULL,
    `ReadAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`MessageId`, `UserId`),
    KEY `IX_MessageReads_UserId` (`UserId`),
    CONSTRAINT `FK_MessageReads_Messages_MessageId` FOREIGN KEY (`MessageId`) REFERENCES `Messages` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_MessageReads_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;", cancellationToken);
    }

    public async Task<List<int>> MarkChatAsReadAsync(int chatId, long userId, CancellationToken cancellationToken = default)
    {
        var ids = await context.Messages.AsNoTracking()
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone && m.SenderId != context.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefault())
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return [];

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var readIds = new List<int>();
            var readAt = IranTime.Now;
            foreach (var messageId in ids)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT IGNORE INTO MessageReads (MessageId, UserId, ReadAt) VALUES (@messageId, @userId, @readAt)";
                AddParameter(insert, "@messageId", messageId);
                AddParameter(insert, "@userId", userId);
                AddParameter(insert, "@readAt", readAt);
                var affected = await insert.ExecuteNonQueryAsync(cancellationToken);
                if (affected > 0) readIds.Add(messageId);
            }
            return readIds;
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    public async Task<List<int>> GetReadMessageIdsForSenderAsync(int chatId, long senderId, CancellationToken cancellationToken = default)
    {
        var senderUsername = await context.Users.AsNoTracking()
            .Where(u => u.Id == senderId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(senderUsername)) return [];

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT DISTINCT m.Id
FROM Messages m
JOIN Chats c ON c.Id = m.ChatId
JOIN MessageReads mr ON mr.MessageId = m.Id
JOIN Users reader ON reader.Id = mr.UserId
WHERE m.ChatId = @chatId
  AND m.SenderId = @senderUsername
  AND m.DeletedForEveryone = 0
  AND reader.Id <> @senderId
  AND FIND_IN_SET(reader.Username, REPLACE(c.Members, ', ', ',')) > 0
ORDER BY m.Id";

            AddParameter(command, "@chatId", chatId);
            AddParameter(command, "@senderId", senderId);
            AddParameter(command, "@senderUsername", senderUsername);

            var result = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(reader.GetInt32(0));

            return result;
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    public async Task<Dictionary<int, int>> GetUnreadCountsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var username = await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(username)) return [];
        var result = new Dictionary<int, int>();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT m.ChatId, COUNT(*)
FROM Messages m
JOIN Chats c ON c.Id = m.ChatId
LEFT JOIN MessageReads r ON r.MessageId = m.Id AND r.UserId = @userId
WHERE c.IsDeleted = 0
  AND (c.Members = @username OR c.Members LIKE CONCAT(@username, ', %') OR c.Members LIKE CONCAT('%, ', @username, ', %') OR c.Members LIKE CONCAT('%, ', @username))
  AND m.DeletedForEveryone = 0
  AND m.SenderId <> @username
  AND r.MessageId IS NULL
GROUP BY m.ChatId";
            AddParameter(command, "@userId", userId);
            AddParameter(command, "@username", username);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result[reader.GetInt32(0)] = reader.GetInt32(1);
            return result;
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    public async Task<bool> AreAllRecipientsReadAsync(int messageId, long senderId, CancellationToken cancellationToken = default)
    {
        var username = await context.Users.AsNoTracking().Where(u => u.Id == senderId).Select(u => u.Username).FirstOrDefaultAsync(cancellationToken);
        var chatId = await context.Messages.AsNoTracking().Where(m => m.Id == messageId).Select(m => (int?)m.ChatId).FirstOrDefaultAsync(cancellationToken);
        var chat = chatId.HasValue ? await context.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken) : null;
        if (string.IsNullOrWhiteSpace(username) || chat == null) return false;
        var memberUsernames = (chat.Members ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.Equals(x, username, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (memberUsernames.Count == 0) return false;
        var recipientIds = await context.Users.AsNoTracking().Where(u => memberUsernames.Contains(u.Username)).Select(u => u.Id).ToListAsync(cancellationToken);
        if (recipientIds.Count != memberUsernames.Count) return false;
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(DISTINCT UserId) FROM MessageReads WHERE MessageId=@messageId";
            AddParameter(command, "@messageId", messageId);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return count >= recipientIds.Count;
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static void AddParameter(IDbCommand command, string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
}