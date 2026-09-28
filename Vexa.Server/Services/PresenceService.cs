using System.Collections.Concurrent;

namespace NovaChat.Server.Services;

public class PresenceService
{
    private readonly ConcurrentDictionary<string, int> _connections = new(StringComparer.OrdinalIgnoreCase);

    public bool UserConnected(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var count = _connections.AddOrUpdate(
            userId.Trim(),
            1,
            (_, current) => current + 1);

        return count == 1;
    }

    public bool UserDisconnected(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        userId = userId.Trim();

        while (true)
        {
            if (!_connections.TryGetValue(userId, out var current))
                return false;

            if (current <= 1)
            {
                if (_connections.TryRemove(userId, out _))
                    return true;
                continue;
            }

            if (_connections.TryUpdate(userId, current - 1, current))
                return false;
        }
    }

    public bool IsOnline(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return _connections.ContainsKey(userId.Trim());
    }

    public IReadOnlyCollection<string> GetOnlineUsers()
    {
        return _connections.Keys.ToArray();
    }
}