using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ChatService
{
    private static readonly SemaphoreSlim CreateChatLock = new(1, 1);
    private readonly AppDbContext _context;

    public ChatService(AppDbContext context) => _context = context;

    public Task<bool> UserExistsAsync(long userId) => _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId);
    public Task<User?> GetUserByUsernameAsync(string username) => _context.Users.FirstOrDefaultAsync(u => u.Username == username.Trim().ToLowerInvariant());
    public Task SaveChangesAsync() => _context.SaveChangesAsync();

    public async Task<Chat?> FindPrivateChatAsync(long firstUserId, long secondUserId)
    {
        if (firstUserId <= 0 || secondUserId <= 0 || firstUserId == secondUserId) return null;
        var names = await _context.Users.AsNoTracking()
            .Where(u => u.Id == firstUserId || u.Id == secondUserId)
            .OrderBy(u => u.Id)
            .Select(u => u.Username)
            .ToListAsync();
        if (names.Count != 2) return null;
        return (await _context.Chats
            .Where(c => c.Type == ChatType.Private && !c.IsDeleted)
            .OrderByDescending(c => c.Id)
            .ToListAsync())
            .FirstOrDefault(c => HasExactlyMembers(c.Members, names));
    }

    public async Task<Chat?> CreatePrivateChatAsync(long currentUserId, long otherUserId)
    {
        if (currentUserId <= 0 || otherUserId <= 0 || currentUserId == otherUserId) return null;
        var users = await _context.Users.Where(u => u.Id == currentUserId || u.Id == otherUserId).ToListAsync();
        if (users.Count != 2) return null;
        var names = users.OrderBy(u => u.Id).Select(u => u.Username).ToList();
        var members = string.Join(", ", names);

        await CreateChatLock.WaitAsync();
        try
        {
            var chats = await _context.Chats.Where(c => c.Type == ChatType.Private && !c.IsDeleted).OrderByDescending(c => c.Id).ToListAsync();
            var existing = chats.FirstOrDefault(c => HasExactlyMembers(c.Members, names));
            if (existing != null)
            {
                existing.Type = ChatType.Private;
                existing.Name = "Private Chat";
                existing.Members = members;
                await _context.SaveChangesAsync();
                PopulatePrivateProjection(existing, users);
                PopulateCompatibilityMembers(existing, users);
                return existing;
            }

            var chat = new Chat { Type = ChatType.Private, Name = "Private Chat", Members = members, CreatedByUserId = currentUserId, IsDeleted = false, DeletedAt = null };
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();
            PopulatePrivateProjection(chat, users);
            PopulateCompatibilityMembers(chat, users);
            return chat;
        }
        finally { CreateChatLock.Release(); }
    }

    public async Task<Chat?> CreateGroupChatAsync(long creatorId, string name, IEnumerable<string> usernames)
    {
        name = NormalizeGroupName(name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return null;

        var normalized = (usernames ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var creator = await _context.Users.FirstOrDefaultAsync(u => u.Id == creatorId);
        if (creator == null) return null;

        // The creator is always included. Group privacy applies only to other selected users.
        if (!normalized.Contains(creator.Username, StringComparer.OrdinalIgnoreCase))
            normalized.Insert(0, creator.Username);

        var users = await _context.Users
            .Where(u => normalized.Contains(u.Username))
            .ToListAsync();

        if (!users.Any(u => u.Id == creatorId)) return null;

        // Defense in depth: re-check privacy at the service boundary. A protected
        // member is skipped rather than being allowed to abort the whole group creation.
        var eligibleUsers = users
            .Where(u => u.Id == creatorId || u.AllowGroupAdds)
            .ToList();

        var eligibleUsernames = eligibleUsers
            .OrderBy(u => u.Id == creatorId ? 0 : 1)
            .ThenBy(u => u.Username)
            .Select(u => u.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!eligibleUsernames.Contains(creator.Username, StringComparer.OrdinalIgnoreCase))
            eligibleUsernames.Insert(0, creator.Username);

        var chat = new Chat
        {
            Type = ChatType.Group,
            Name = name,
            Members = string.Join(", ", eligibleUsernames),
            CreatedByUserId = creatorId,
            IsDeleted = false,
            DeletedAt = null
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();
        PopulateCompatibilityMembers(chat, eligibleUsers);
        return chat;
    }

    public async Task<List<Chat>> GetUserChatsAsync(long userId)
    {
        var username = await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(username)) return [];
        var chats = await _context.Chats.AsNoTracking().Where(c => !c.IsDeleted && (c.Members == username || c.Members.StartsWith(username + ", ") || c.Members.Contains(", " + username + ", ") || c.Members.EndsWith(", " + username))).OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync();
        foreach (var chat in chats)
        {
            await PopulateCompatibilityMembersAsync(chat);
            await PopulatePrivateProjectionAsync(chat);
            var last = await GetLastMessageAsync(chat.Id, userId);
            chat.Messages = last == null ? [] : [last];
        }
        return chats.OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt).ThenByDescending(c => c.Id).ToList();
    }

    public async Task<List<Chat>> GetAllChatsAsync()
    {
        var chats = await _context.Chats.AsNoTracking().Where(c => !c.IsDeleted).OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync();
        foreach (var chat in chats)
        {
            await PopulateCompatibilityMembersAsync(chat);
            await PopulatePrivateProjectionAsync(chat);
            var last = await GetLastMessageAsync(chat.Id);
            chat.Messages = last == null ? [] : [last];
        }
        return chats.OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt).ThenByDescending(c => c.Id).ToList();
    }

    public async Task<Chat?> GetChatByIdAsync(int chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);
        if (chat != null)
        {
            await PopulateCompatibilityMembersAsync(chat);
            await PopulatePrivateProjectionAsync(chat);
        }
        return chat;
    }

    private async Task PopulatePrivateProjectionAsync(Chat chat)
    {
        var usernames = ParseMembers(chat.Members).Take(2).ToList();
        if (usernames.Count == 0) return;
        var users = await _context.Users.AsNoTracking().Where(u => usernames.Contains(u.Username)).ToListAsync();
        PopulatePrivateProjection(chat, users);
    }

    private static void PopulatePrivateProjection(Chat chat, IEnumerable<User> users)
    {
        if (chat.Type != ChatType.Private) return;
        var ordered = ParseMembers(chat.Members).Select(n => users.FirstOrDefault(u => u.Username == n)).Where(u => u != null).Cast<User>().Take(2).ToList();
        chat.User1Id = ordered.ElementAtOrDefault(0)?.Id;
        chat.User2Id = ordered.ElementAtOrDefault(1)?.Id;
        chat.User1 = ordered.ElementAtOrDefault(0);
        chat.User2 = ordered.ElementAtOrDefault(1);
    }

    private async Task PopulateCompatibilityMembersAsync(Chat chat)
    {
        var usernames = ParseMembers(chat.Members).ToList();
        if (usernames.Count == 0) { chat.ChatMembers.Clear(); return; }
        var users = await _context.Users.AsNoTracking().Where(u => usernames.Contains(u.Username)).ToListAsync();
        PopulateCompatibilityMembers(chat, users);
    }

    private static void PopulateCompatibilityMembers(Chat chat, IEnumerable<User> users)
    {
        chat.ChatMembers.Clear();
        var lookup = users.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var username in ParseMembers(chat.Members))
        {
            if (!lookup.TryGetValue(username, out var user)) continue;
            chat.ChatMembers.Add(new ChatMember { Id = ++index, ChatId = chat.Id, UserId = user.Id, User = user, Chat = chat, Role = user.Id == chat.CreatedByUserId ? (int)ChatMemberRole.Owner : (int)ChatMemberRole.Member, JoinedAt = chat.CreatedAt });
        }
    }

    public async Task<bool> CanAccessChatAsync(int chatId, long userId)
    {
        var username = await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(username)) return false;
        return await _context.Chats.AnyAsync(c => c.Id == chatId && !c.IsDeleted && (c.Members == username || c.Members.StartsWith(username + ", ") || c.Members.Contains(", " + username + ", ") || c.Members.EndsWith(", " + username)));
    }

    public async Task<Message?> SendMessageAsync(int chatId, long senderId, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !await CanAccessChatAsync(chatId, senderId)) return null;
        var sender = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == senderId);
        if (sender == null) return null;
        var message = new Message { ChatId = chatId, SenderId = sender.Username, Content = content.Trim() };
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<Message?> EditMessageAsync(int messageId, long userId, string encryptedContent)
    {
        if (messageId <= 0 || string.IsNullOrWhiteSpace(encryptedContent))
            return null;

        var username = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(username))
            return null;

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && !m.DeletedForEveryone);

        if (message == null ||
            !string.Equals(message.SenderId, username, StringComparison.OrdinalIgnoreCase) ||
            !await CanAccessChatAsync(message.ChatId, userId))
            return null;

        // Media messages have their own encrypted envelope and must not be replaced
        // by a text-message envelope.
        if (MediaMessageEnvelope.TryParse(message.Content, out _) ||
            E2eeMediaMessageEnvelope.TryParse(message.Content, out _))
            return null;

        message.Content = encryptedContent.Trim();
        message.EditedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return message;
    }

    public async Task<List<Message>> GetMessagesAsync(int chatId, long viewerUserId, int? beforeMessageId = null, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Messages.AsNoTracking().Where(m => m.ChatId == chatId && !m.DeletedForEveryone);
        if (beforeMessageId.HasValue)
        {
            var before = await _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == beforeMessageId.Value && m.ChatId == chatId && !m.DeletedForEveryone);
            if (before != null) query = query.Where(m => m.SentAt < before.SentAt || (m.SentAt == before.SentAt && m.Id < before.Id));
        }
        var messages = await query.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(pageSize + 1).ToListAsync();
        messages = messages.Where(m => !IsDeletedForUser(m, viewerUserId)).ToList();
        if (messages.Count > pageSize) messages.RemoveAt(messages.Count - 1);
        return messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();
    }

    public async Task<bool> HasOlderMessagesAsync(int chatId, long viewerUserId, int firstMessageId)
    {
        var first = await _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == firstMessageId && m.ChatId == chatId && !m.DeletedForEveryone);
        if (first == null || IsDeletedForUser(first, viewerUserId)) return false;
        var older = await _context.Messages.AsNoTracking().Where(m => m.ChatId == chatId && !m.DeletedForEveryone && (m.SentAt < first.SentAt || (m.SentAt == first.SentAt && m.Id < first.Id))).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(101).ToListAsync();
        return older.Any(m => !IsDeletedForUser(m, viewerUserId));
    }

    public Task<Message?> GetLastMessageAsync(int chatId) => _context.Messages.AsNoTracking().Where(m => m.ChatId == chatId && !m.DeletedForEveryone).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync();
    public async Task<Message?> GetLastMessageAsync(int chatId, long viewerUserId)
    {
        var messages = await _context.Messages.AsNoTracking().Where(m => m.ChatId == chatId && !m.DeletedForEveryone).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(100).ToListAsync();
        return messages.FirstOrDefault(m => !IsDeletedForUser(m, viewerUserId));
    }
    public Task<Message?> GetMessageByIdAsync(int messageId) => _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId);

    public async Task<List<ChatMember>> GetMembersAsync(int chatId)
    {
        var chat = await GetChatByIdAsync(chatId);
        return chat?.ChatMembers.ToList() ?? [];
    }

    public async Task<ChatMember?> GetMemberAsync(int chatId, long userId)
    {
        var chat = await GetChatByIdAsync(chatId);
        return chat?.ChatMembers.FirstOrDefault(m => m.UserId == userId);
    }

    public async Task<bool> AddMemberAsync(int chatId, long actorId, long userId)
    {
        if (!await IsGroupAsync(chatId) || !await CanAccessChatAsync(chatId, actorId) || !await UserExistsAsync(userId)) return false;
        var target = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);
        if (target == null || chat == null || !target.AllowGroupAdds) return false;
        var members = ParseMembers(chat.Members).ToList();
        if (!members.Contains(target.Username, StringComparer.OrdinalIgnoreCase))
        {
            members.Add(target.Username);
            chat.Members = string.Join(", ", members);
            await _context.SaveChangesAsync();
        }
        return true;
    }

    public async Task<bool> RemoveMemberAsync(int chatId, long actorId, long userId)
    {
        if (!await IsGroupAsync(chatId) || !await CanAccessChatAsync(chatId, actorId)) return false;
        var target = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);
        if (target == null || chat == null || chat.CreatedByUserId == userId) return false;
        var currentMembers = ParseMembers(chat.Members).ToList();
        var members = currentMembers.Where(x => !string.Equals(x, target.Username, StringComparison.OrdinalIgnoreCase)).ToList();
        if (members.Count == currentMembers.Count) return false;
        chat.Members = string.Join(", ", members);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> LeaveGroupAsync(int chatId, long userId)
    {
        if (!await IsGroupAsync(chatId)) return false;
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);
        if (user == null || chat == null || chat.CreatedByUserId == userId) return false;
        var members = ParseMembers(chat.Members).ToList();
        if (members.RemoveAll(x => string.Equals(x, user.Username, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        chat.Members = string.Join(", ", members);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RenameGroupAsync(int chatId, long actorId, string name)
    {
        name = NormalizeGroupName(name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return false;
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == ChatType.Group && !c.IsDeleted);
        if (chat == null || !await CanAccessChatAsync(chatId, actorId)) return false;
        chat.Name = name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteChatAsync(int chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && !c.IsDeleted);
        if (chat == null) return false;
        chat.IsDeleted = true;
        chat.DeletedAt = IranTime.Now;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteMessageAsync(int messageId)
    {
        var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return false;
        _context.Messages.Remove(message);
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<int?> GetMessageChatIdAsync(int messageId) => _context.Messages.Where(m => m.Id == messageId).Select(m => (int?)m.ChatId).FirstOrDefaultAsync();
    private Task<bool> IsGroupAsync(int chatId) => _context.Chats.AnyAsync(c => c.Id == chatId && c.Type == ChatType.Group && !c.IsDeleted);
    private static bool HasExactlyMembers(string stored, IReadOnlyCollection<string> expected) { var actual = ParseMembers(stored).ToHashSet(StringComparer.OrdinalIgnoreCase); return actual.Count == expected.Count && expected.All(actual.Contains); }
    private static IEnumerable<string> ParseMembers(string members) => (members ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase);
    private static string NormalizeGroupName(string name) { name = name.Trim(); const string prefix = "Group - "; if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name[prefix.Length..].Trim(); return name; }
    private static bool IsDeletedForUser(Message message, long userId) => string.IsNullOrWhiteSpace(message.DeletedForUserIds) ? false : message.DeletedForUserIds.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => long.TryParse(x, out var id) && id == userId);
}
