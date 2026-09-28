using Microsoft.AspNetCore.SignalR.Client;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly bool _messageDeletionRealtimeRegistered = RegisterMessageDeletionRealtime();

    private HubConnection? _messageDeletionAttachedConnection;
    private int _messageDeletionAttachAttempts;

    private static bool RegisterMessageDeletionRealtime()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMessageDeletionRealtimeLoaded));
        return true;
    }

    private static void OnMessageDeletionRealtimeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
        {
            view._messageDeletionAttachAttempts = 0;
            view.QueueMessageDeletionHandlerAttach();
        }
    }

    private void QueueMessageDeletionHandlerAttach()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(AttachMessageDeletionHandlerWhenReady));
    }

    private async void AttachMessageDeletionHandlerWhenReady()
    {
        if (_hubConnection != null && !ReferenceEquals(_messageDeletionAttachedConnection, _hubConnection))
        {
            _hubConnection.On<RealtimeMessageDeletedPayload>("MessageDeleted", OnRealtimeMessageDeleted);
            _messageDeletionAttachedConnection = _hubConnection;
            return;
        }

        if (_hubConnection == null && _messageDeletionAttachAttempts++ < 50 && IsLoaded)
        {
            await Task.Delay(100);
            if (IsLoaded)
                QueueMessageDeletionHandlerAttach();
        }
    }

    private async void OnRealtimeMessageDeleted(RealtimeMessageDeletedPayload payload)
    {
        if (payload.Id <= 0 || payload.ChatId <= 0)
            return;

        await Dispatcher.InvokeAsync(async () =>
        {
            if (_currentChatId != payload.ChatId)
            {
                _loadedMessageIds.Remove(payload.Id);
                return;
            }

            _loadedMessageIds.Remove(payload.Id);

            var bubble = MessagesPanel.Children
                .OfType<Border>()
                .FirstOrDefault(x => x.Tag is int id && id == payload.Id);

            if (bubble != null)
            {
                MessagesPanel.Children.Remove(bubble);
            }
            else
            {
                var candidates = MessagesPanel.Children
                    .OfType<Border>()
                    .Where(IsMessageBubble)
                    .Where(x => BubbleMatchesDeletedMessage(x, payload))
                    .ToList();

                if (candidates.Count > 0)
                    MessagesPanel.Children.Remove(candidates[^1]);
            }

            // Reload from the server as a guaranteed fallback. This also prevents
            // a soft-deleted message from becoming an empty bubble after refresh.
            await ReloadCurrentChatMessagesAfterDeletionAsync(payload.ChatId);
            await LoadChatsAsync();
        }, DispatcherPriority.Normal);
    }

    private async Task ReloadCurrentChatMessagesAfterDeletionAsync(int chatId)
    {
        if (_currentChatId != chatId)
            return;

        MessagesPanel.Children.Clear();
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;
        UpdateLoadOlderButton();

        await LoadInitialMessagesAsync(chatId);
        await ScrollMessagesToBottomAsync();
    }

    private static bool IsMessageBubble(Border border) =>
        border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Any();

    private static bool BubbleMatchesDeletedMessage(Border border, RealtimeMessageDeletedPayload payload)
    {
        if (border.Child is not StackPanel panel)
            return false;

        var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
        var time = panel.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();

        return text != null && time != null
            && string.Equals(text.Text, payload.Content, StringComparison.Ordinal)
            && string.Equals(time.Text, payload.SentAt.ToLocalTime().ToString("HH:mm"), StringComparison.Ordinal);
    }

    private sealed class RealtimeMessageDeletedPayload
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }
}
