namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly object NotificationStateLock = new();
    private static int? _activeChatId;

    internal static void ResetActiveChat()
    {
        lock (NotificationStateLock)
            _activeChatId = null;
    }

    internal static void SetActiveChat(int chatId)
    {
        if (chatId <= 0) return;

        lock (NotificationStateLock)
            _activeChatId = chatId;
    }

    internal static void ClearActiveChat(int chatId)
    {
        lock (NotificationStateLock)
        {
            if (_activeChatId == chatId)
                _activeChatId = null;
        }
    }

    internal static bool IsCurrentChat(int chatId)
    {
        if (chatId <= 0) return false;

        lock (NotificationStateLock)
            return _activeChatId.HasValue && _activeChatId.Value == chatId;
    }
}
