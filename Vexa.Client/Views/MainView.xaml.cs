using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView : UserControl
{
    public event Action? ProfileRequested;
    public event Action? SettingsRequested;
    private const int MessagePageSize = 50;
    private readonly ApiService _apiService = new();
    private readonly E2eeCryptoService _e2ee = new();
    private HubConnection? _hubConnection;
    private readonly List<ChatListItem> _chats = [];
    private readonly HashSet<int> _loadedMessageIds = [];
    private readonly HashSet<string> _onlineUserIds = new(StringComparer.OrdinalIgnoreCase);
    private int? _currentChatId;
    private int? _oldestLoadedMessageId;
    private string _currentOtherUserId = string.Empty;
    private bool _isOwner;
    private bool _isLoadingOlderMessages;
    private bool _hasMoreMessages;
    private bool _isOpeningChat;
    private static bool _messageDeletionHandlerRegistered;

    public MainView()
    {
        InitializeComponent(); ResetActiveChat(); InitializeConversationAvatarFix();
        if (!_messageDeletionHandlerRegistered) { EventManager.RegisterClassHandler(typeof(Border), UIElement.MouseRightButtonUpEvent, new MouseButtonEventHandler(OnMessageBubbleRightClick)); _messageDeletionHandlerRegistered = true; }
        SetOwnerMode(false); Loaded += MainView_Loaded; Unloaded += MainView_Unloaded; StartMessageDeletionHook();
    }
    private async void MainView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_currentChatId.HasValue) SetActiveChat(_currentChatId.Value);
        try { await _e2ee.InitializeAsync(_apiService); }
        catch (Exception ex) { MessageBox.Show($"Could not initialize end-to-end encryption.\n\n{ex.Message}", "NovaChat Security", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        try { await LoadChatsAsync(); } catch (Exception ex) { MessageBox.Show($"Could not load chats.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
        try { await ConnectSignalRAsync(); } catch (Exception ex) { ChatStatusText.Text = "Offline"; ChatStatusIndicator.Fill = Brushes.Gray; _hubConnection = null; System.Diagnostics.Debug.WriteLine($"SignalR connection failed: {ex}"); }
    }
    private async void MainView_Unloaded(object sender, RoutedEventArgs e) { try { StopMessageDeletionHook(); await DisconnectSignalRAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SignalR disconnect failed during unload: {ex}"); } }
    public void SetOwnerMode(bool isOwner) { _isOwner = isOwner; AccountTypeText.Text = isOwner ? "OWNER • Full Access" : "User Account"; OwnerBottomPanel.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed; }
    private async Task ConnectSignalRAsync()
    {
        if (!AuthState.IsAuthenticated || _hubConnection != null) return;
        _hubConnection = new HubConnectionBuilder().WithUrl("http://localhost:5256/hubs/chat", o => o.AccessTokenProvider = () => Task.FromResult(AuthState.Token)!).WithAutomaticReconnect().Build();
        _hubConnection.On<MessageModel>("ReceiveMessage", OnMessageReceived);
        _hubConnection.On<MessageKeyRequestedPayload>("MessageKeyRequested", OnMessageKeyRequested);
        _hubConnection.On<MessageKeyDeliveredPayload>("MessageKeyDelivered", OnMessageKeyDelivered);
        _hubConnection.On<List<string>>("PresenceSnapshot", OnPresenceSnapshot);
        _hubConnection.On<string>("UserOnline", OnUserOnline);
        _hubConnection.On<string>("UserOffline", OnUserOffline);
        _hubConnection.On<ChatDeletedEvent>("ChatDeleted", OnChatDeleted);
        RegisterReadReceiptHandlers();
        _hubConnection.Reconnecting += OnSignalRReconnecting; _hubConnection.Reconnected += OnSignalRReconnected; _hubConnection.Closed += OnSignalRClosed;
        await _hubConnection.StartAsync();
        ChatStatusText.Text = "Connected";
        await RefreshCurrentUserPresenceAsync();
        // Now that the secure channel is active, retry chat previews so historical
        // messages missing this device's key can trigger key recovery automatically.
        await LoadChatsAsync();
    }
    private Task OnSignalRReconnecting(Exception? _) => Dispatcher.InvokeAsync(() => { ChatStatusText.Text = "Connecting..."; ChatStatusIndicator.Fill = Brushes.Gray; }).Task;
    private async Task OnSignalRReconnected(string? _) { ChatStatusText.Text = "Connected"; await RefreshCurrentUserPresenceAsync(); }
    private Task OnSignalRClosed(Exception? _) => Dispatcher.InvokeAsync(() => { ChatStatusText.Text = "Offline"; ChatStatusIndicator.Fill = Brushes.Gray; _onlineUserIds.Clear(); RefreshPresenceUi(); }).Task;
    private async Task DisconnectSignalRAsync() { var connection = _hubConnection; _hubConnection = null; if (connection != null) { try { await connection.StopAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SignalR StopAsync failed: {ex}"); } try { await connection.DisposeAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SignalR DisposeAsync failed: {ex}"); } } _onlineUserIds.Clear(); if (!IsLoaded) return; ChatStatusText.Text = "Offline"; ChatStatusIndicator.Fill = Brushes.Gray; RefreshPresenceUi(); }
    private async Task RefreshCurrentUserPresenceAsync() { if (_hubConnection?.State != HubConnectionState.Connected) return; try { var users = await _hubConnection.InvokeAsync<List<string>>("GetOnlineUsers"); await Dispatcher.InvokeAsync(() => { _onlineUserIds.Clear(); foreach (var id in users ?? []) if (!string.IsNullOrWhiteSpace(id)) _onlineUserIds.Add(id); RefreshPresenceUi(); }); } catch { } }
    private async void OnPresenceSnapshot(List<string> ids) => await Dispatcher.InvokeAsync(() => { _onlineUserIds.Clear(); foreach (var id in ids ?? []) if (!string.IsNullOrWhiteSpace(id)) _onlineUserIds.Add(id); RefreshPresenceUi(); });
    private async void OnUserOnline(string id) { if (!string.IsNullOrWhiteSpace(id)) await Dispatcher.InvokeAsync(() => { _onlineUserIds.Add(id); RefreshPresenceUi(); }); }
    private async void OnUserOffline(string id) { if (!string.IsNullOrWhiteSpace(id)) await Dispatcher.InvokeAsync(() => { _onlineUserIds.Remove(id); RefreshPresenceUi(); }); }
    private async void OnChatDeleted(ChatDeletedEvent evt) { if (evt == null || evt.ChatId <= 0) return; await Dispatcher.InvokeAsync(async () => { try { await RemoveChatFromLocalUiAsync(evt.ChatId); } catch { } }); }
    private bool IsUserOnline(string id) => !string.IsNullOrWhiteSpace(id) && _onlineUserIds.Contains(id);
    private void RefreshPresenceUi() { foreach (var item in _chats) item.IsOnline = IsUserOnline(item.Chat.OtherUserId(AuthState.UserId)); RefreshChatsList(); UpdateCurrentChatPresence(); }
    private void UpdateCurrentChatPresence() { if (string.IsNullOrWhiteSpace(_currentOtherUserId)) { ChatStatusText.Text = "Offline"; ChatStatusIndicator.Fill = Brushes.Gray; return; } var online = IsUserOnline(_currentOtherUserId); ChatStatusText.Text = online ? "Online" : "Offline"; ChatStatusIndicator.Fill = online ? Brushes.LimeGreen : Brushes.Gray; }
    private async void OnMessageReceived(MessageModel message)
    {
        if (message == null || message.Id <= 0 || message.ChatId <= 0) return;
        try { message = await DecryptMessageWithRecoveryAsync(message); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"E2EE message decryption failed: {ex}"); message.Content = "[Encrypted message — unable to decrypt]"; }
        await Dispatcher.InvokeAsync(() =>
        {
            var isOwnMessage = string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase);
            var isCurrentChat = _currentChatId == message.ChatId || IsCurrentChat(message.ChatId);
            if (isCurrentChat)
            {
                var currentItem = _chats.FirstOrDefault(x => x.Chat.Id == message.ChatId);
                if (currentItem != null) currentItem.UnreadCount = 0;
            }
            else if (!isOwnMessage) ShowIncomingMessageNotification(message);
            if (!isCurrentChat) { UpdateChatPreview(message); return; }
            if (!_loadedMessageIds.Add(message.Id)) return;
            AddMessageToUi(message); UpdateChatPreview(message); _ = ScrollMessagesToBottomAsync();
        });
    }
    private void ShowIncomingMessageNotification(MessageModel message)
    {
        try
        {
            var chat = _chats.FirstOrDefault(x => x.Chat.Id == message.ChatId);
            var isGroup = chat?.Chat.IsGroup == true;
            var title = isGroup ? (string.IsNullOrWhiteSpace(chat?.DisplayName) ? "NovaChat" : chat!.DisplayName) : (string.IsNullOrWhiteSpace(message.SenderName) ? "New message" : message.SenderName);
            var preview = BuildNotificationPreview(message.Content);
            var body = isGroup && !string.IsNullOrWhiteSpace(message.SenderName) ? $"{message.SenderName}: {preview}" : preview;
            NotificationService.ShowMessageNotification(message.ChatId, title, body);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Incoming message notification failed: {ex}"); }
    }
    private static string BuildNotificationPreview(string? content) { var text = string.IsNullOrWhiteSpace(content) ? "New message" : content.Trim(); const int maxLength = 140; return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…"; }
    private async Task LoadChatsAsync()
    {
        var chats = await _apiService.GetAsync<List<ChatModel>>("api/Chat"); _chats.Clear();
        foreach (var chat in chats ?? [])
        {
            if (chat.LastMessage != null) chat.LastMessage = await _e2ee.DecryptMessageAsync(chat.LastMessage);
            var avatar = await LoadProfileAvatarForChatAsync(chat);
            var item = new ChatListItem { Chat = chat, DisplayName = chat.OtherUserName(AuthState.UserId), LastMessage = chat.LastMessage == null ? "No messages yet." : FormatLastMessage(chat.LastMessage), IsOnline = IsUserOnline(chat.OtherUserId(AuthState.UserId)), AvatarSource = avatar, UnreadCount = _currentChatId == chat.Id ? 0 : chat.UnreadCount }; _chats.Add(item);
        }
        RefreshChatsList(); await Dispatcher.InvokeAsync(async () => await RefreshConversationAvatarsAsync(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private async Task<BitmapImage?> LoadProfileAvatarForChatAsync(ChatModel chat) { var otherUserId = chat.OtherUserId(AuthState.UserId); if (string.IsNullOrWhiteSpace(otherUserId)) return null; try { var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(otherUserId)}"); if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarUrl)) return null; if (string.Equals(chat.User1Id, otherUserId, StringComparison.OrdinalIgnoreCase)) chat.User1AvatarUrl = profile.AvatarUrl; else chat.User2AvatarUrl = profile.AvatarUrl; return await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(profile.AvatarUrl)); } catch { return null; } }
    private void RefreshChatsList() { ChatsList.ItemsSource = null; ChatsList.ItemsSource = _chats; NoChatsText.Visibility = _chats.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private string FormatLastMessage(MessageModel message) => (string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase) ? "You: " : "") + message.Content;
    private void UpdateChatPreview(MessageModel message) { var item = _chats.FirstOrDefault(x => x.Chat.Id == message.ChatId); if (item == null) return; var isCurrentChat = _currentChatId == message.ChatId || IsCurrentChat(message.ChatId); item.Chat.LastMessage = message; item.LastMessage = FormatLastMessage(message); if (isCurrentChat) item.UnreadCount = 0; else if (!string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase)) item.UnreadCount++; var index = _chats.IndexOf(item); if (index > 0) { _chats.RemoveAt(index); _chats.Insert(0, item); } RefreshChatsList(); _ = Dispatcher.InvokeAsync(RefreshConversationAvatarsAsync, System.Windows.Threading.DispatcherPriority.Loaded); }
    private async void NewChatButton_Click(object sender, RoutedEventArgs e) { var dialog = new Window { Title = "New Chat", Width = 400, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize, Background = (Brush)FindResource("PanelBackgroundBrush") }; var box = new TextBox { Margin = new Thickness(20), Height = 40, Padding = new Thickness(10) }; var button = new Button { Content = "Start Chat", Width = 100, Height = 35, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20), Background = (Brush)FindResource("PrimaryBrush"), Foreground = Brushes.White }; var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "Enter Username", Margin = new Thickness(20, 20, 0, 0), Foreground = (Brush)FindResource("TextBrush") }); panel.Children.Add(box); panel.Children.Add(button); dialog.Content = panel; string? username = null; button.Click += (_, _) => { username = box.Text.Trim(); if (!string.IsNullOrWhiteSpace(username)) dialog.DialogResult = true; }; box.KeyDown += (_, e) => { if (e.Key == Key.Enter) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }; dialog.Loaded += (_, _) => box.Focus(); dialog.ShowDialog(); if (string.IsNullOrWhiteSpace(username) || string.Equals(username, AuthState.Username, StringComparison.OrdinalIgnoreCase)) return; await OpenChatWithUsernameAsync(username); }
    private async void ChatButton_Click(object sender, RoutedEventArgs e) { if (sender is Button { DataContext: ChatListItem item }) await OpenChatAsync(item.Chat); }
    private async Task OpenChatAsync(ChatModel chat) { CancelForwardMode(); if (_isOpeningChat) return; _isOpeningChat = true; try { if (_currentChatId.HasValue && _hubConnection?.State == HubConnectionState.Connected) try { await _hubConnection.InvokeAsync("LeaveChat", _currentChatId.Value); } catch { } _currentChatId = chat.Id; SetActiveChat(chat.Id); var openedItem = _chats.FirstOrDefault(x => x.Chat.Id == chat.Id); if (openedItem != null) openedItem.UnreadCount = 0; _currentOtherUserId = chat.OtherUserId(AuthState.UserId); ChatUserNameText.Text = chat.OtherUserName(AuthState.UserId); UpdateCurrentChatPresence(); MessagesPanel.Children.Clear(); _loadedMessageIds.Clear(); _oldestLoadedMessageId = null; _hasMoreMessages = false; UpdateLoadOlderButton(); if (_hubConnection?.State == HubConnectionState.Connected) await _hubConnection.InvokeAsync("JoinChat", chat.Id); await LoadInitialMessagesAsync(chat.Id); await ScrollMessagesToBottomAsync(); await RefreshCurrentUserAvatarAsync(); } catch (Exception ex) { MessageBox.Show($"Could not open chat.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); } finally { _isOpeningChat = false; } }
    private async Task LoadInitialMessagesAsync(int chatId)
    {
        var response = await _apiService.GetAsync<ChatHistoryResponse>($"api/Chat/{chatId}/messages?pageSize={MessagePageSize}"); if (response == null) return;
        foreach (var message in response.Messages.OrderBy(x => x.SentAt))
            if (_loadedMessageIds.Add(message.Id))
                AddMessageToUi(await DecryptMessageWithRecoveryAsync(message));
        _oldestLoadedMessageId = response.NextBeforeMessageId; _hasMoreMessages = response.HasMore; UpdateLoadOlderButton();
    }
    private async Task LoadOlderMessagesAsync() { if (!_currentChatId.HasValue || !_hasMoreMessages || _isLoadingOlderMessages || !_oldestLoadedMessageId.HasValue) return; _isLoadingOlderMessages = true; try { var oldHeight = MessagesScrollViewer.ExtentHeight; var oldOffset = MessagesScrollViewer.VerticalOffset; var response = await _apiService.GetAsync<ChatHistoryResponse>($"api/Chat/{_currentChatId.Value}/messages?beforeMessageId={_oldestLoadedMessageId.Value}&pageSize={MessagePageSize}"); if (response == null) return; foreach (var message in response.Messages.OrderByDescending(x => x.SentAt)) if (_loadedMessageIds.Add(message.Id)) AddMessageToUi(await _e2ee.DecryptMessageAsync(message), true); await Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToVerticalOffset(oldOffset + (MessagesScrollViewer.ExtentHeight - oldHeight)), System.Windows.Threading.DispatcherPriority.Loaded); _oldestLoadedMessageId = response.NextBeforeMessageId; _hasMoreMessages = response.HasMore; } catch (Exception ex) { MessageBox.Show($"Could not load older messages.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); } finally { _isLoadingOlderMessages = false; UpdateLoadOlderButton(); } }
    private void UpdateLoadOlderButton() { LoadOlderMessagesButton.Visibility = _hasMoreMessages ? Visibility.Visible : Visibility.Collapsed; LoadOlderMessagesButton.IsEnabled = !_isLoadingOlderMessages; }
    private void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (e.VerticalOffset <= 30 && _hasMoreMessages && !_isLoadingOlderMessages) _ = LoadOlderMessagesAsync(); }
    private async void LoadOlderMessagesButton_Click(object sender, RoutedEventArgs e) => await LoadOlderMessagesAsync();
    private void DeleteChatButton_Click(object sender, RoutedEventArgs e) { }
    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardMessage != null)
            await ForwardPendingMessageAsync();
        else
            await SendCurrentMessageAsync();
    }

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_forwardMessage != null)
            await ForwardPendingMessageAsync();
        else
            await SendCurrentMessageAsync();
    }

    private async Task SendCurrentMessageAsync()
    {
        if (!_currentChatId.HasValue) { MessageBox.Show("Please select a chat first.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var content = MessageTextBox.Text.Trim(); if (string.IsNullOrWhiteSpace(content)) return;
        if (_hubConnection?.State != HubConnectionState.Connected) { MessageBox.Show("Real-time connection is not available.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            var encrypted = await _e2ee.EncryptForChatAsync(_currentChatId.Value, content, _apiService);
            MessageTextBox.Clear();
            await _hubConnection.InvokeAsync("SendMessage", _currentChatId.Value, encrypted);
        }
        catch (Exception ex) { MessageBox.Show($"Could not send message.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private sealed class ChatDeletedEvent { public int ChatId { get; set; } public string? DeletedBy { get; set; } }
}
