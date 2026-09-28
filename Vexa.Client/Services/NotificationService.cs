namespace NovaChat.Client.Services;

/// <summary>
/// Message notifications are intentionally disabled in NovaChat.
/// The class remains as a no-op compatibility layer so existing client code
/// does not need to change and the rest of the application remains stable.
/// </summary>
public static class NotificationService
{
    public static void ShowMessageNotification(int chatId, string title, string message)
    {
        // Notifications are disabled by design.
    }

    public static void Dispose()
    {
        // Notifications are disabled by design; nothing to clean up.
    }
}
