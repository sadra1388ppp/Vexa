using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class GroupAddRequestService(AppDbContext db)
{
    private readonly AppDbContext _db = db;
    private async Task EnsureSchemaAsync(CancellationToken cancellationToken) => await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `GroupAddRequests` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `GroupId` INT NOT NULL,
    `RequesterUserId` BIGINT NOT NULL,
    `TargetUserId` BIGINT NOT NULL,
    `Status` VARCHAR(20) NOT NULL DEFAULT 'Pending',
    `CreatedAt` DATETIME(6) NOT NULL,
    `RespondedAt` DATETIME(6) NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_GroupAddRequests_Pending` (`GroupId`, `RequesterUserId`, `TargetUserId`, `Status`),
    INDEX `IX_GroupAddRequests_Target_Status` (`TargetUserId`, `Status`),
    INDEX `IX_GroupAddRequests_Requester_Status` (`RequesterUserId`, `Status`),
    CONSTRAINT `FK_GroupAddRequests_Group` FOREIGN KEY (`GroupId`) REFERENCES `Chats`(`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GroupAddRequests_Requester` FOREIGN KEY (`RequesterUserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GroupAddRequests_Target` FOREIGN KEY (`TargetUserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", cancellationToken);

    public async Task<(bool Success, string Message, GroupAddRequestDto? Request, bool AddedImmediately)> CreateAsync(int groupId, long requesterId, string username, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var group = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == groupId && c.Type == ChatType.Group && !c.IsDeleted, cancellationToken);
        var requester = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == requesterId, cancellationToken);
        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim().ToLowerInvariant(), cancellationToken);
        if (group == null || requester == null || target == null) return (false, "Group or user not found.", null, false);
        if (group.CreatedByUserId != requesterId) return (false, "Only the group owner can add members.", null, false);
        if (target.Id == requesterId) return (false, "You are already the group owner.", null, false);

        var members = ParseMembers(group.Members);
        if (members.Contains(target.Username, StringComparer.OrdinalIgnoreCase)) return (true, "This user is already a member of the group.", null, true);

        if (target.AllowGroupAdds)
        {
            members.Add(target.Username);
            var trackedGroup = await _db.Chats.FirstOrDefaultAsync(c => c.Id == groupId && !c.IsDeleted, cancellationToken);
            if (trackedGroup == null) return (false, "Group not found.", null, false);
            trackedGroup.Members = string.Join(", ", members);
            await _db.SaveChangesAsync(cancellationToken);
            return (true, $"@{target.Username} was added to the group.", null, true);
        }

        var pending = await _db.Database.SqlQueryRaw<long>(
            "SELECT `Id` AS `Value` FROM `GroupAddRequests` WHERE `GroupId` = {0} AND `RequesterUserId` = {1} AND `TargetUserId` = {2} AND `Status` = 'Pending' ORDER BY `Id` DESC LIMIT 1",
            groupId, requesterId, target.Id).SingleOrDefaultAsync(cancellationToken);
        if (pending > 0) return (true, $"@{target.Username} does not allow people to add them to groups. A request is already pending.", await GetByIdAsync(pending, cancellationToken), false);

        var now = IranTime.Now;
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO `GroupAddRequests` (`GroupId`,`RequesterUserId`,`TargetUserId`,`Status`,`CreatedAt`)
VALUES ({groupId},{requesterId},{target.Id},{"Pending"},{now});", cancellationToken);
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            var current = await _db.Database.SqlQueryRaw<long>(
                "SELECT `Id` AS `Value` FROM `GroupAddRequests` WHERE `GroupId` = {0} AND `RequesterUserId` = {1} AND `TargetUserId` = {2} AND `Status` = 'Pending' ORDER BY `Id` DESC LIMIT 1",
                groupId, requesterId, target.Id).SingleOrDefaultAsync(cancellationToken);
            if (current > 0) return (true, $"@{target.Username} does not allow people to add them to groups. A request is already pending.", await GetByIdAsync(current, cancellationToken), false);
            throw;
        }

        var id = await _db.Database.SqlQueryRaw<long>("SELECT LAST_INSERT_ID() AS `Value`").SingleAsync(cancellationToken);
        return (true, $"@{target.Username} does not allow people to add them to groups. A request was sent.", await GetByIdAsync(id, cancellationToken), false);
    }

    public async Task<List<GroupAddRequestDto>> GetIncomingAsync(long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var rows = await _db.Database.SqlQueryRaw<GroupAddRequestRow>(@"
SELECT r.Id, r.GroupId, c.Name AS GroupName, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM GroupAddRequests r
JOIN Chats c ON c.Id = r.GroupId
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.TargetUserId = {0} AND r.Status = 'Pending' AND c.IsDeleted = 0
ORDER BY r.CreatedAt DESC, r.Id DESC", userId).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<List<GroupAddRequestDto>> GetOutgoingAsync(long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var rows = await _db.Database.SqlQueryRaw<GroupAddRequestRow>(@"
SELECT r.Id, r.GroupId, c.Name AS GroupName, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM GroupAddRequests r
JOIN Chats c ON c.Id = r.GroupId
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.RequesterUserId = {0} AND r.Status = 'Pending' AND c.IsDeleted = 0
ORDER BY r.CreatedAt DESC, r.Id DESC", userId).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<(bool Success, string Message, GroupAddRequestDto? Request, int? GroupId)> AcceptAsync(long requestId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var row = await LoadRowAsync(requestId, cancellationToken);
        if (row == null) return (false, "Group request not found.", null, null);
        if (row.TargetUserId != userId) return (false, "You cannot respond to this request.", null, null);
        if (!string.Equals(row.Status, "Pending", StringComparison.OrdinalIgnoreCase)) return (false, "This request is no longer pending.", ToDto(row), row.GroupId);

        var group = await _db.Chats.FirstOrDefaultAsync(c => c.Id == row.GroupId && c.Type == ChatType.Group && !c.IsDeleted, cancellationToken);
        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (group == null || target == null) return (false, "The group no longer exists.", null, null);

        var members = ParseMembers(group.Members);
        if (!members.Contains(target.Username, StringComparer.OrdinalIgnoreCase))
        {
            members.Add(target.Username);
            group.Members = string.Join(", ", members);
        }

        var now = IranTime.Now;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE `GroupAddRequests`
SET `Status` = {"Accepted"}, `RespondedAt` = {now}
WHERE `Id` = {requestId} AND `Status` = {"Pending"};", cancellationToken);
        row.Status = "Accepted";
        row.RespondedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return (true, "Group request accepted. You were added to the group.", ToDto(row), row.GroupId);
    }

    public async Task<(bool Success, string Message, GroupAddRequestDto? Request)> RejectAsync(long requestId, long userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var row = await LoadRowAsync(requestId, cancellationToken);
        if (row == null) return (false, "Group request not found.", null);
        if (row.TargetUserId != userId) return (false, "You cannot respond to this request.", null);
        if (!string.Equals(row.Status, "Pending", StringComparison.OrdinalIgnoreCase)) return (false, "This request is no longer pending.", ToDto(row));
        var now = IranTime.Now;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE `GroupAddRequests` SET `Status` = {"Rejected"}, `RespondedAt` = {now}
WHERE `Id` = {requestId} AND `Status` = {"Pending"};", cancellationToken);
        row.Status = "Rejected";
        row.RespondedAt = now;
        return (true, "Group request rejected.", ToDto(row));
    }

    private async Task<GroupAddRequestDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => (await LoadRowAsync(id, cancellationToken)) is { } row ? ToDto(row) : null;

    private async Task<GroupAddRequestRow?> LoadRowAsync(long requestId, CancellationToken cancellationToken) => await _db.Database.SqlQueryRaw<GroupAddRequestRow>(@"
SELECT r.Id, r.GroupId, c.Name AS GroupName, r.RequesterUserId, r.TargetUserId, r.Status, r.CreatedAt, r.RespondedAt,
       ru.Username AS RequesterUsername, ru.DisplayName AS RequesterDisplayName,
       tu.Username AS TargetUsername, tu.DisplayName AS TargetDisplayName
FROM GroupAddRequests r
JOIN Chats c ON c.Id = r.GroupId
JOIN Users ru ON ru.Id = r.RequesterUserId
JOIN Users tu ON tu.Id = r.TargetUserId
WHERE r.Id = {0}
LIMIT 1", requestId).SingleOrDefaultAsync(cancellationToken);

    private static List<string> ParseMembers(string members) => (members ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static GroupAddRequestDto ToDto(GroupAddRequestRow row) => new()
    {
        Id = row.Id, GroupId = row.GroupId, GroupName = row.GroupName,
        RequesterUserId = row.RequesterUserId.ToString(), RequesterUsername = row.RequesterUsername, RequesterDisplayName = row.RequesterDisplayName,
        TargetUserId = row.TargetUserId.ToString(), TargetUsername = row.TargetUsername, TargetDisplayName = row.TargetDisplayName,
        Status = row.Status, CreatedAt = row.CreatedAt, RespondedAt = row.RespondedAt
    };

    private sealed class GroupAddRequestRow
    {
        public long Id { get; set; }
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public long RequesterUserId { get; set; }
        public long TargetUserId { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
    }
}