using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class OwnerUserChatsWindow
{
    private readonly E2eeCryptoService _ownerEnhancedE2ee = new();
    private bool _ownerEnhancedHooked;

    static OwnerUserChatsWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(OwnerUserChatsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnEnhancedLoaded));
    }

    private static void OnEnhancedLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not OwnerUserChatsWindow window || window._ownerEnhancedHooked) return;
        window._ownerEnhancedHooked = true;
        window.ChatsList.SelectionChanged += window.EnhancedChatsList_SelectionChanged;
        _ = window.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => _ = window.RenderEnhancedCurrentChatAsync()));
    }

    private async void EnhancedChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Dispatcher.InvokeAsync(async () => await RenderEnhancedCurrentChatAsync(), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task RenderEnhancedCurrentChatAsync()
    {
        if (ChatsList.SelectedItem is not AdminChatModel chat) return;

        try
        {
            await _ownerEnhancedE2ee.InitializeAsync(_apiService);
            var history = await _apiService.GetAsync<EnhancedOwnerMessagesResponse>($"api/OwnerChat/{chat.Id}/messages?pageSize=1000");
            var messages = history?.Messages ?? [];

            MessagesPanel.Children.Clear();

            foreach (var original in messages)
            {
                var rawContent = original.Content;
                var encryptedMedia = rawContent.StartsWith("__NOVACHAT_E2EE_MEDIA__", StringComparison.Ordinal);
                var message = original;
                await _ownerEnhancedE2ee.DecryptMessageAsync(message);

                var element = await OwnerMessageRenderer.BuildAsync(
                    this,
                    _apiService,
                    chat.Id,
                    message.Id,
                    string.Equals(message.SenderId, _userId, StringComparison.OrdinalIgnoreCase)
                        ? $"{_displayName}  •  @{_userId}"
                        : message.SenderName,
                    message.SenderId,
                    message.Content,
                    message.SentAt,
                    message.MessageType,
                    message.FileName,
                    message.ContentType,
                    message.FileSize,
                    message.DurationSeconds,
                    encryptedMedia,
                    async () =>
                    {
                        if (encryptedMedia) return null;
                        return await _apiService.GetBytesAsync($"api/OwnerMedia/{chat.Id}/{message.Id}");
                    });

                MessagesPanel.Children.Add(element);
            }

            if (messages.Count == 0)
            {
                MessagesPanel.Children.Add(new TextBlock
                {
                    Text = "No messages in this conversation.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20),
                    Foreground = FindBrush("SecondaryTextBrush")
                });
            }

            await Dispatcher.InvokeAsync(
                () => MessagesScrollViewer.ScrollToEnd(),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Enhanced User Manager message rendering failed: {ex}");
        }
    }

    private sealed class EnhancedOwnerMessagesResponse
    {
        public List<MessageModel> Messages { get; set; } = [];
        public int Count { get; set; }
    }
}
