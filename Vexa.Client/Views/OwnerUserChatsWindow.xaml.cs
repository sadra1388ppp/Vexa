using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class OwnerUserChatsWindow : Window
{
    private readonly ApiService _apiService = new();
    private readonly string _userId;
    private readonly string _displayName;
    private List<AdminChatModel> _chats = [];
    private AdminChatModel? _selectedChat;
    private bool _busy;

    public OwnerUserChatsWindow(string userId, string displayName)
    {
        InitializeComponent();
        _userId = userId;
        _displayName = displayName;
        TitleText.Text = $"Chats of {displayName}";
        SubtitleText.Text = $"Owner control • @{userId} • Read and manage messages";
        Loaded += OwnerUserChatsWindow_Loaded;
    }

    private async void OwnerUserChatsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OwnerUserChatsWindow_Loaded;
        await LoadChatsAsync();
    }

    private async Task LoadChatsAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _chats = await _apiService.GetAsync<List<AdminChatModel>>($"api/Admin/users/{Uri.EscapeDataString(_userId)}/chats") ?? [];
            ChatsList.ItemsSource = null;
            ChatsList.ItemsSource = _chats;
            if (_chats.Count > 0)
                ChatsList.SelectedIndex = 0;
            else
                ClearConversation();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the user's chats.\n\n{ex.Message}", "Owner Chat Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void ChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsList.SelectedItem is not AdminChatModel chat) return;
        _selectedChat = chat;
        ConversationTitleText.Text = chat.OtherDisplayName;
        ConversationMetaText.Text = $"@{chat.OtherUsername}  •  {chat.MessageCount} message{(chat.MessageCount == 1 ? "" : "s")}  •  Chat #{chat.Id}";
        await LoadMessagesAsync(chat.Id);
    }

    private async Task LoadMessagesAsync(int chatId)
    {
        try
        {
            var messages = await _apiService.GetAsync<List<MessageModel>>($"api/Admin/chats/{chatId}/messages") ?? [];
            RenderMessages(messages);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load messages.\n\n{ex.Message}", "Owner Chat Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenderMessages(List<MessageModel> messages)
    {
        MessagesPanel.Children.Clear();
        foreach (var message in messages)
        {
            var outer = new Border
            {
                Background = FindBrush("InputBackgroundBrush"),
                BorderBrush = FindBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 9),
                Tag = message.Id
            };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = string.Equals(message.SenderId, _userId, StringComparison.OrdinalIgnoreCase)
                    ? $"{_displayName}  •  @{_userId}"
                    : message.SenderName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("SecondaryTextBrush")
            });
            body.Children.Add(new TextBlock
            {
                Text = message.Content,
                FontSize = 14,
                Foreground = FindBrush("TextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 8, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = message.SentAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm:ss", CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = FindBrush("SecondaryTextBrush"),
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(body, 0);
            root.Children.Add(body);

            var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            var edit = new Button { Content = "Edit", Width = 58, Height = 30, Margin = new Thickness(8, 0, 0, 5), Style = FindStyle("SecondaryButtonStyle") };
            edit.Click += async (_, _) => await EditMessageAsync(message);
            var delete = new Button { Content = "Delete", Width = 58, Height = 30, Style = FindStyle("DangerButtonStyle") };
            delete.Click += async (_, _) => await DeleteMessageAsync(message);
            actions.Children.Add(edit);
            actions.Children.Add(delete);
            Grid.SetColumn(actions, 1);
            root.Children.Add(actions);

            outer.Child = root;
            MessagesPanel.Children.Add(outer);
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

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => MessagesScrollViewer.ScrollToEnd()));
    }

    private async Task EditMessageAsync(MessageModel message)
    {
        if (_busy) return;
        var dialog = new Window
        {
            Title = "Edit message",
            Width = 520,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = FindBrush("PanelBackgroundBrush")
        };

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "Edit message", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = FindBrush("TextBrush") });
        root.Children.Add(new TextBlock { Text = "The edited content will be sent to both chat participants.", FontSize = 12, Foreground = FindBrush("SecondaryTextBrush"), Margin = new Thickness(0, 4, 0, 12) });
        var box = new TextBox { Text = message.Content, Height = 92, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, Background = FindBrush("InputBackgroundBrush"), Foreground = FindBrush("TextBrush"), BorderBrush = FindBrush("BorderBrush") };
        root.Children.Add(box);
        var feedback = new TextBlock { Foreground = FindBrush("DangerBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 5) };
        root.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 82, Height = 36, Margin = new Thickness(0, 0, 8, 0), Style = FindStyle("SecondaryButtonStyle") };
        var save = new Button { Content = "Save", Width = 82, Height = 36, Style = FindStyle("PrimaryButtonStyle") };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            var content = box.Text.Trim();
            if (string.IsNullOrWhiteSpace(content)) { feedback.Text = "Message cannot be empty."; return; }
            save.IsEnabled = false; cancel.IsEnabled = false;
            try
            {
                var response = await _apiService.PutAsync<EditMessageRequest, ActionResponse>($"api/Admin/messages/{message.Id}", new EditMessageRequest { Content = content });
                if (response == null) throw new InvalidOperationException("The server returned no response.");
                dialog.Close();
                if (_selectedChat != null) await LoadMessagesAsync(_selectedChat.Id);
                await RefreshChatsAsync();
            }
            catch (Exception ex)
            {
                feedback.Text = ex.Message;
                save.IsEnabled = true; cancel.IsEnabled = true;
            }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        root.Children.Add(actions);
        dialog.Content = root;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        dialog.ShowDialog();
    }

    private async Task DeleteMessageAsync(MessageModel message)
    {
        if (_busy) return;
        if (MessageBox.Show("Delete this message for everyone?\n\nThis action cannot be undone.", "Delete message", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            _busy = true;
            await _apiService.DeleteAsync($"api/Admin/messages/{message.Id}");
            if (_selectedChat != null) await LoadMessagesAsync(_selectedChat.Id);
            await RefreshChatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the message.\n\n{ex.Message}", "Owner Chat Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RefreshChatsAsync()
    {
        _chats = await _apiService.GetAsync<List<AdminChatModel>>($"api/Admin/users/{Uri.EscapeDataString(_userId)}/chats") ?? [];
        var selectedId = _selectedChat?.Id;
        ChatsList.ItemsSource = null;
        ChatsList.ItemsSource = _chats;
        if (selectedId.HasValue)
            ChatsList.SelectedItem = _chats.FirstOrDefault(x => x.Id == selectedId.Value);
    }

    private void ClearConversation()
    {
        _selectedChat = null;
        ConversationTitleText.Text = "Select a conversation";
        ConversationMetaText.Text = string.Empty;
        MessagesPanel.Children.Clear();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private Brush FindBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    private Style? FindStyle(string key) => TryFindResource(key) as Style;

    private sealed class AdminChatModel
    {
        public int Id { get; set; }
        public string OtherUserId { get; set; } = string.Empty;
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherDisplayName { get; set; } = string.Empty;
        public string? OtherAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int MessageCount { get; set; }
        public MessageModel? LastMessage { get; set; }
    }


    private sealed class EditMessageRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ActionResponse
    {
        public string? Message { get; set; }
        public MessageModel? Data { get; set; }
    }
}
