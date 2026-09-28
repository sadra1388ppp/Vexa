using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class AllChatsView : UserControl
{
    public event Action? BackToChatRequested;

    private readonly ApiService _apiService = new();
    private readonly E2eeCryptoService _e2ee = new();
    private readonly ObservableCollection<AdminChatItem> _allChats = [];
    private readonly ObservableCollection<AdminChatItem> _filteredChats = [];
    private bool _isLoading;

    public AllChatsView()
    {
        InitializeComponent();
        ChatsList.ItemsSource = _filteredChats;
        Loaded += AllChatsView_Loaded;
    }

    private async void AllChatsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            await _e2ee.InitializeAsync(_apiService);
            await LoadChatsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not initialize secure Owner access.";
            MessageBox.Show($"Could not initialize secure Owner access.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadChatsAsync()
    {
        StatusText.Text = "Loading all conversations...";

        try
        {
            var chats = await _apiService.GetAsync<List<ChatModel>>("api/Chat/all") ?? [];
            foreach (var chat in chats)
            {
                if (chat.LastMessage != null)
                    await _e2ee.DecryptMessageAsync(chat.LastMessage);
            }

            _allChats.Clear();
            foreach (var chat in chats)
                _allChats.Add(new AdminChatItem(chat));

            ApplyFilter();
            TotalChatsText.Text = _allChats.Count.ToString();
            StatusText.Text = _allChats.Count == 0
                ? "No conversations are currently stored on the server."
                : $"Showing {_filteredChats.Count} of {_allChats.Count} conversations.";
        }
        catch (Exception ex)
        {
            _allChats.Clear();
            ApplyFilter();
            TotalChatsText.Text = "0";
            StatusText.Text = "Could not load conversations.";
            MessageBox.Show($"Could not load all chats.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        IEnumerable<AdminChatItem> filtered = _allChats;
        if (!string.IsNullOrWhiteSpace(query))
            filtered = _allChats.Where(c => c.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));

        _filteredChats.Clear();
        foreach (var item in filtered)
            _filteredChats.Add(item);

        FilteredCountText.Text = $"{_filteredChats.Count} conversation{(_filteredChats.Count == 1 ? string.Empty : "s")}";
        EmptyText.Visibility = _filteredChats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ClearDetails();
        _isLoading = true;
        try
        {
            await LoadChatsAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();

    private async void ChatRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AdminChatItem item)
        {
            ChatsList.SelectedItem = item;
            await ShowChatDetailsAsync(item);
        }
    }

    private async void ChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsList.SelectedItem is AdminChatItem item)
            await ShowChatDetailsAsync(item);
    }

    private async Task ShowChatDetailsAsync(AdminChatItem item)
    {
        var kind = item.Chat.IsGroup ? "GROUP" : "PRIVATE CHAT";
        SelectedChatIdText.Text = $"{kind}  •  Chat #{item.Chat.Id}  •  Created {IranTime.Format(item.Chat.CreatedAt)}";
        DeleteButton.IsEnabled = true;
        DeleteButton.Content = item.Chat.IsGroup ? "Delete Group" : "Delete Chat";
        NoMessagesText.Visibility = Visibility.Collapsed;
        MessagesList.ItemsSource = null;
        ParticipantsText.Text = item.Chat.IsGroup
            ? "Loading group members..."
            : $"{item.Chat.User1Name}  ·  ID {item.Chat.User1Id}\n{item.Chat.User2Name}  ·  ID {item.Chat.User2Id}";
        StatusText.Text = $"Loading details for {kind.ToLowerInvariant()} #{item.Chat.Id}...";

        try
        {
            // OwnerChat has a dedicated owner-only endpoint. This avoids relying
            // on the normal member-access path and guarantees that the global
            // Owner can inspect groups even when not a group member.
            var members = await _apiService.GetAsync<List<OwnerMemberModel>>($"api/OwnerChat/{item.Chat.Id}/members") ?? [];

            if (item.Chat.IsGroup)
            {
                ParticipantsText.Text = members.Count == 0
                    ? "No members found."
                    : string.Join("\n", members.Select((m, i) =>
                        $"{i + 1}. {m.DisplayName}  •  @{m.Username}  •  {m.Role}  •  ID {m.UserId}"));
            }
            else if (members.Count > 0)
            {
                ParticipantsText.Text = string.Join("\n", members.Select(m =>
                    $"{m.DisplayName}  •  @{m.Username}  •  ID {m.UserId}"));
            }

            var ownerHistory = await _apiService.GetAsync<OwnerMessagesResponse>($"api/OwnerChat/{item.Chat.Id}/messages?pageSize=1000");
            var messages = ownerHistory?.Messages ?? [];

            // The server returns only the encrypted envelope. Decrypting here
            // guarantees the message plaintext never travels through the API.
            foreach (var message in messages)
            {
                await _e2ee.DecryptMessageAsync(message);
                if (message.IsDeletedForEveryone)
                    message.Content = "این پیام را کاربر فرستنده پاک کرده است.";
            }

            MessagesList.ItemsSource = messages.Select(m => new AdminMessageItem(m)).ToList();
            NoMessagesText.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"{kind} #{item.Chat.Id} • {members.Count} participant{(members.Count == 1 ? string.Empty : "s")} • {messages.Count} message{(messages.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            NoMessagesText.Visibility = Visibility.Visible;
            StatusText.Text = "Could not load conversation details.";
            MessageBox.Show($"Could not load this conversation.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatsList.SelectedItem is not AdminChatItem item) return;
        if (!await ShowProfessionalDeleteConfirmationAsync(item)) return;

        try
        {
            DeleteButton.IsEnabled = false;
            StatusText.Text = item.Chat.IsGroup ? "Deleting group..." : "Deleting conversation...";

            if (!await _apiService.DeleteAsync($"api/OwnerChat/{item.Chat.Id}"))
            {
                MessageBox.Show(item.Chat.IsGroup ? "The server could not delete this group." : "The server could not delete this conversation.", "All Chats", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _allChats.Remove(item);
            _filteredChats.Remove(item);
            TotalChatsText.Text = _allChats.Count.ToString();
            ApplyFilter();
            ClearDetails();
            StatusText.Text = item.Chat.IsGroup ? "Group deleted successfully." : "Conversation deleted successfully.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Delete failed.";
            MessageBox.Show($"Could not delete the {(item.Chat.IsGroup ? "group" : "conversation")}.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeleteButton.IsEnabled = ChatsList.SelectedItem is AdminChatItem;
        }
    }

    private async Task<bool> ShowProfessionalDeleteConfirmationAsync(AdminChatItem item)
    {
        var isGroup = item.Chat.IsGroup;
        var title = isGroup ? "Delete group?" : "Delete conversation?";
        var name = isGroup ? (string.IsNullOrWhiteSpace(item.Chat.Name) ? "Unnamed group" : item.Chat.Name) : $"{item.Chat.User1Name} ↔ {item.Chat.User2Name}";
        var description = isGroup
            ? "You are about to permanently remove this group, its membership and its message history from NovaChat."
            : "You are about to permanently remove this private conversation and its message history from NovaChat.";
        var warning = isGroup
            ? "Every member will lose access to the group and all messages in it. This action cannot be undone."
            : "This action affects the server-side conversation and cannot be undone.";

        var dialog = new Window
        {
            Title = isGroup ? "Delete Group" : "Delete Conversation",
            Width = 500,
            Height = 370,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = GetBrush("AppBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(16), Background = GetBrush("PrimarySoftBrush") };
        icon.Child = new TextBlock { Text = isGroup ? "♟" : "⌫", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(icon);
        var titlePanel = new StackPanel { Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.Bold, Foreground = GetBrush("TextBrush") });
        titlePanel.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = GetBrush("SecondaryTextBrush"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        header.Children.Add(titlePanel);
        Grid.SetRow(header, 0); root.Children.Add(header);

        var separator = new Border { Height = 1, Background = GetBrush("BorderBrush"), Margin = new Thickness(0, 20, 0, 16) };
        Grid.SetRow(separator, 1); root.Children.Add(separator);

        var explanation = new TextBlock { Text = description, FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = GetBrush("TextBrush") };
        Grid.SetRow(explanation, 2); root.Children.Add(explanation);

        var warningCard = new Border { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(13, 11, 13, 11), CornerRadius = new CornerRadius(10), Background = GetBrush("InputBackgroundBrush"), BorderBrush = GetBrush("BorderBrush"), BorderThickness = new Thickness(1) };
        warningCard.Child = new TextBlock { Text = warning, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = GetBrush("SecondaryTextBrush") };
        Grid.SetRow(warningCard, 3); root.Children.Add(warningCard);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 100, Height = 40, Margin = new Thickness(0, 0, 9, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        var confirm = new Button { Content = isGroup ? "Delete group" : "Delete chat", Width = 125, Height = 40, Style = (Style)FindResource("DangerButtonStyle") };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm);
        Grid.SetRow(actions, 4); root.Children.Add(actions);

        dialog.Content = root;
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }

    private void ClearDetails()
    {
        ChatsList.SelectedItem = null;
        SelectedChatIdText.Text = string.Empty;
        ParticipantsText.Text = string.Empty;
        MessagesList.ItemsSource = null;
        NoMessagesText.Visibility = Visibility.Collapsed;
        DeleteButton.IsEnabled = false;
        DeleteButton.Content = "Delete Chat";
    }

    private Brush GetBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private sealed class AdminChatItem
    {
        public ChatModel Chat { get; }
        public string Participants => Chat.IsGroup
            ? $"GROUP  •  {Chat.Name}"
            : $"PRIVATE  •  {Chat.User1Name}  ↔  {Chat.User2Name}";
        public string Preview => Chat.LastMessage == null
            ? (Chat.IsGroup ? "No group messages yet" : "No messages yet")
            : $"{Chat.LastMessage.SenderName}: {Chat.LastMessage.Content}";
        public string LastActivityText => IranTime.Format(Chat.LastMessage?.SentAt ?? Chat.CreatedAt);
        public string Initials
        {
            get
            {
                if (Chat.IsGroup) return GetInitials(Chat.Name);
                var a = GetInitials(Chat.User1Name);
                var b = GetInitials(Chat.User2Name);
                return $"{a}{b}";
            }
        }
        public string SearchText => string.Join(" ", Chat.IsGroup ? "group" : "private", Chat.Name, Chat.User1Id, Chat.User2Id, Chat.User1Name, Chat.User2Name, Chat.LastMessage?.Content ?? string.Empty, Chat.LastMessage?.SenderName ?? string.Empty);

        public AdminChatItem(ChatModel chat) => Chat = chat;

        private static string GetInitials(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "?";
            var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
            return $"{parts[0][..1]}{parts[^1][..1]}".ToUpperInvariant();
        }
    }

    private sealed class OwnerMemberModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private sealed class OwnerMessagesResponse
    {
        public List<MessageModel> Messages { get; set; } = [];
        public int Count { get; set; }
    }

    private sealed class AdminMessageItem
    {
        public string SenderName { get; }
        public string Content { get; }
        public string TimeText { get; }

        public AdminMessageItem(MessageModel message)
        {
            SenderName = string.IsNullOrWhiteSpace(message.SenderName) ? message.SenderId : message.SenderName;
            Content = string.IsNullOrWhiteSpace(message.Content) ? "[No text content]" : message.Content;
            TimeText = message.IsDeletedForEveryone
                ? $"{IranTime.Format(message.SentAt)}  •  DELETED BY SENDER"
                : IranTime.Format(message.SentAt);
        }
    }
}
