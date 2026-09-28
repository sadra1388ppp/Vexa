using Microsoft.AspNetCore.SignalR.Client;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private DispatcherTimer? _messageDeletionHookTimer;
    private HubConnection? _messageDeletionHookedConnection;

    private void StartMessageDeletionHook()
    {
        if (_messageDeletionHookTimer != null)
            return;

        _messageDeletionHookTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _messageDeletionHookTimer.Tick += MessageDeletionHookTimer_Tick;
        _messageDeletionHookTimer.Start();
    }

    private void StopMessageDeletionHook()
    {
        if (_messageDeletionHookTimer == null)
            return;

        _messageDeletionHookTimer.Stop();
        _messageDeletionHookTimer.Tick -= MessageDeletionHookTimer_Tick;
        _messageDeletionHookTimer = null;
        _messageDeletionHookedConnection = null;
    }

    private void MessageDeletionHookTimer_Tick(object? sender, EventArgs e)
    {
        var connection = _hubConnection;
        if (connection == null || connection.State != HubConnectionState.Connected)
            return;

        if (ReferenceEquals(_messageDeletionHookedConnection, connection))
            return;

        _messageDeletionHookedConnection = connection;
        connection.On<MessageDeletedEvent>("MessageDeleted", OnMessageDeletedLive);
    }

    private async Task OnMessageDeletedLive(MessageDeletedEvent deleted)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (_currentChatId != deleted.ChatId)
                return;

            var root = MessagesPanel.Children
                .OfType<Border>()
                .FirstOrDefault(x => x.Tag is int id && id == deleted.Id);

            if (root != null)
            {
                MessagesPanel.Children.Remove(root);
                _loadedMessageIds.Remove(deleted.Id);
            }

            _ = LoadChatsAsync();
        });
    }

    private sealed class MessageDeletedEvent
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public string? SenderId { get; set; }
        public string? Content { get; set; }
        public DateTime SentAt { get; set; }
    }
}
