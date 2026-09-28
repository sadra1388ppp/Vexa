using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class AllChatsView
{
    private readonly E2eeCryptoService _ownerEnhancedE2ee = new();
    private bool _ownerEnhancedHooked;

    static AllChatsView()
    {
        EventManager.RegisterClassHandler(
            typeof(AllChatsView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnEnhancedLoaded));
    }

    private static void OnEnhancedLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not AllChatsView view || view._ownerEnhancedHooked) return;
        view._ownerEnhancedHooked = true;
        view.ChatsList.SelectionChanged += view.EnhancedChatsList_SelectionChanged;
        _ = view.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => _ = view.RenderEnhancedCurrentChatAsync()));
    }

    private async void EnhancedChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Dispatcher.InvokeAsync(async () => await RenderEnhancedCurrentChatAsync(), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task RenderEnhancedCurrentChatAsync()
    {
        if (ChatsList.SelectedItem is not AdminChatItem item) return;

        try
        {
            await _ownerEnhancedE2ee.InitializeAsync(_apiService);
            var history = await _apiService.GetAsync<OwnerMessagesResponse>($"api/OwnerChat/{item.Chat.Id}/messages?pageSize=1000");
            var messages = history?.Messages ?? [];

            MessagesList.ItemsSource = null;
            MessagesList.ItemTemplate = null;
            MessagesList.Items.Clear();

            foreach (var original in messages)
            {
                var rawContent = original.Content;
                var encryptedMedia = rawContent.StartsWith("__NOVACHAT_E2EE_MEDIA__", StringComparison.Ordinal);
                var message = original;
                await _ownerEnhancedE2ee.DecryptMessageAsync(message);

                if (message.IsDeletedForEveryone)
                {
                    message.Content = "این پیام را کاربر فرستنده پاک کرده است.";
                    message.MessageType = "text";
                    message.FileName = null;
                    message.ContentType = null;
                    message.FileSize = null;
                    message.DurationSeconds = null;
                    message.AttachmentUrl = null;
                }

                var element = await OwnerMessageRenderer.BuildAsync(
                    this,
                    _apiService,
                    item.Chat.Id,
                    message.Id,
                    message.SenderName,
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
                        return await _apiService.GetBytesAsync($"api/OwnerMedia/{item.Chat.Id}/{message.Id}");
                    });

                MessagesList.Items.Add(element);
            }

            NoMessagesText.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Enhanced All Chats message rendering failed: {ex}");
        }
    }
}
