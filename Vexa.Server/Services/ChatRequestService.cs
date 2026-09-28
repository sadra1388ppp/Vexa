using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class ChatRequestService(AppDbContext db, ChatService chatService)
{
    private readonly AppDbContext _db = db;
    private readonly ChatService _chatService = chatService;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE `Users`
    ADD COLUMN IF NOT EXISTS `MessagePrivacy` VARCHAR(20) NOT NULL DEFAULT 'Everybody',
    ADD COLUMN IF NOT EXISTS `AllowGroupAdds` VARCHAR(5) NOT NULL DEFAULT 'true';", cancellationToken);

        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `ChatRequests` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `RequesterUserId` BIGINT NOT NULL,
    `TargetUserId` BIGINT NOT NULL,
    `Status` VARCHAR(20) NOT NULL DEFAULT 'Pending',
    `CreatedAt` DATETIME(6) NOT NULL,
    `RespondedAt` DATETIME(6) NULL,
    `ChatId` INT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_ChatRequests_Pending` (`RequesterUserId`, `TargetUserId`, `Status`),
    INDEX `IX_ChatRequests_Target_Status` (`TargetUserId`, `Status`),
    INDEX `IX_ChatRequests_Requester_Status` (`RequesterUserId`, `Status`),
    CONSTRAINT `FK_ChatRequests_Requester` FOREIGN KEY (`RequesterUserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ChatRequests_Target` FOREIGN KEY (`TargetUserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", cancellationToken);

        await _db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE `ChatRequests`
    DROP INDEX `UX_ChatRequests_Pending`;", cancellationToken);

        await _db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE `ChatRequests`
    ADD COLUMN IF NOT EXISTS `PendingPairKey` VARCHAR(64)
        GENERATED ALWAYS AS (
            CASE
                WHEN `Status` = 'Pending' THEN CONCAT(`RequesterUserId`, '-', `TargetUserId`)
                ELSE NULL
            END
        ) STORED,
    ADD UNIQUE INDEX IF NOT EXISTS `UX_ChatRequests_Pending` (`PendingPairKey`);", cancellationToken);
    }

    public async Task<(bool Success, string Message, ChatRequestDto? Request)> CreateAsync(long requesterId, string username, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var requester = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == requesterId, cancellationToken);
        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim().ToLowerInvariant(), cancellationToken);
        if (requester == null || target == null) return (false, "User not found.", null);
        if (requester.Id == target.Id) return (false, "You cannot send a chat request to yourself.", null);

        var existingChat = await _chatService.FindPrivateChatAsync(requester.Id, target.Id);
        if (existingChat != null) return (true, "A conversation already exists.", null);

        var pending = await _db.Database.SqlQueryRaw<long>(
            "SELECT `Id` AS `Value` FROM `ChatRequests` WHERE `RequesterUserId` = {0} AND `TargetUserId` = {1} AND `Status` = 'Pending' ORDER BY `Id` DESC LIMIT 1",
            requester.Id, target.Id).SingleOrDefaultAsync(cancellationToken);
        if (pending > 0)
        {
            var current = await GetByIdAsync(pending, cancellationToken);
            return (true, "Chat request is already pending.", current);
        }

        var now = IranTime.Now;
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO `ChatRequests` (`RequesterUserId`,`TargetUserId`,`Status`,`CreatedAt`)
VALUES ({requester.Id},{target.Id},{"Pending"},{now});", cancellationToken);
        }
        catch (Exception exception) when (exception is MySqlConnector.MySqlException { Number: 1062 })
        {
            var currentPending = await _db.Database.SqlQueryRaw<long>(
                "SELECT `Id` AS `Value` FROM `ChatRequests` WHERE `RequesterUserId` = {0} AND `TargetUserId` = {1} AND `Status` = 'Pending' ORDER BY `Id` DESC LIMIT 1",
                requester.Id, target.Id).SingleOrDefaultAsync(cancellationToken);
            if (currentPending > 0)
                return (true, "Chat request is already pending.", await GetByIdAsync(currentPending, cancellationToken));
            throw;
        }

        var id = await _db.Database.SqlQueryRaw<long>("SELECT LAST_INSERT_ID() AS `Value`").SingleAsync(cancellationToken);
        var result = new ChatRequestDto
        {
            Id = id,
            RequesterUserId = requester.Id.ToString(),
            RequesterUsername = requester.Username,
            RequesterDisplayName = requester.DisplayName,
            TargetUserId = target.Id.ToString(),
            TargetUsername = target.Username,
            TargetDisplayName = target.DisplayName,
            Status = "Pending",
            CreatedAt = now
        };
        return (true, "Chat request sent.", result);
    }

    public async Task<List<ChatRequestDto>> GetIncomingAsync(long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var rows = await _db.Database.SqlQueryRaw<ChatRequestRow>(@"
SELECT r.Id, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt, r.ChatId,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM ChatRequests r
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.TargetUserId = {0} AND r.Status = 'Pending'
ORDER BY r.CreatedAt ASC, r.Id ASC", userId).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<List<ChatRequestDto>> GetOutgoingAsync(long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var rows = await _db.Database.SqlQueryRaw<ChatRequestRow>(@"
SELECT r.Id, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt, r.ChatId,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM ChatRequests r
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.RequesterUserId = {0} AND r.Status = 'Pending'
ORDER BY r.CreatedAt DESC, r.Id DESC", userId).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<(bool Success, string Message, ChatRequestDto? Request, Chat? Chat)> AcceptAsync(long requestId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<(bool Success, string Message, ChatRequestDto? Request, Chat? Chat)>(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var row = await LoadRowAsync(requestId, cancellationToken);
            if (row == null) return (false, "Chat request not found.", null, null);
            if (row.TargetUserId != userId) return (false, "You cannot respond to this request.", null, null);
            if (!string.Equals(row.Status, "Pending", StringComparison.OrdinalIgnoreCase)) return (false, "This request is no longer pending.", ToDto(row), null);

            var chat = await _chatService.CreatePrivateChatAsync(row.RequesterUserId, row.TargetUserId);
            if (chat == null) return (false, "The conversation could not be created.", null, null);

            var now = IranTime.Now;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE `ChatRequests`
SET `Status` = {"Accepted"}, `RespondedAt` = {now}, `ChatId` = {chat.Id}
WHERE `Id` = {requestId} AND `Status` = {"Pending"};", cancellationToken);

            row.Status = "Accepted";
            row.RespondedAt = now;
            row.ChatId = chat.Id;
            await transaction.CommitAsync(cancellationToken);
            return (true, "Chat request accepted.", ToDto(row), chat);
        });
    }

    public async Task<(bool Success, string Message, ChatRequestDto? Request)> RejectAsync(long requestId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var row = await LoadRowAsync(requestId, cancellationToken);
        if (row == null) return (false, "Chat request not found.", null);
        if (row.TargetUserId != userId) return (false, "You cannot respond to this request.", null);
        if (!string.Equals(row.Status, "Pending", StringComparison.OrdinalIgnoreCase)) return (false, "This request is no longer pending.", ToDto(row));
        var now = IranTime.Now;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE `ChatRequests` SET `Status` = {"Rejected"}, `RespondedAt` = {now}
WHERE `Id` = {requestId} AND `Status` = {"Pending"};", cancellationToken);
        row.Status = "Rejected";
        row.RespondedAt = now;
        return (true, "Chat request rejected.", ToDto(row));
    }

    private async Task<ChatRequestDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        (await LoadRowAsync(id, cancellationToken)) is { } row ? ToDto(row) : null;

    private async Task<ChatRequestRow?> LoadRowAsync(long requestId, CancellationToken cancellationToken)
    {
        return await _db.Database.SqlQueryRaw<ChatRequestRow>(@"
SELECT r.Id, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt, r.ChatId,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM ChatRequests r
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.Id = {0}
LIMIT 1", requestId).SingleOrDefaultAsync(cancellationToken);
    }

    private static ChatRequestDto ToDto(ChatRequestRow row) => new()
    {
        Id = row.Id,
        RequesterUserId = row.RequesterUserId.ToString(),
        RequesterUsername = row.RequesterUsername,
        RequesterDisplayName = row.RequesterDisplayName,
        TargetUserId = row.TargetUserId.ToString(),
        TargetUsername = row.TargetUsername,
        TargetDisplayName = row.TargetDisplayName,
        Status = row.Status,
        CreatedAt = row.CreatedAt,
        RespondedAt = row.RespondedAt,
        ChatId = row.ChatId
    };

    private sealed class ChatRequestRow
    {
        public long Id { get; set; }
        public long RequesterUserId { get; set; }
        public long TargetUserId { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public int? ChatId { get; set; }
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
    }
}