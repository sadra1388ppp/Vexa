using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void ApplyChatSnapshot(IEnumerable<ChatModel>? serverChats)
    {
        if (serverChats == null || _isCreatingChatSafely)
            return;

        var snapshot = serverChats
            .Where(x => x.Id > 0)
            .GroupBy(x => GetConversationKey(x, AuthState.UserId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(x => x.Id).First())
            .ToDictionary(x => GetConversationKey(x, AuthState.UserId), StringComparer.OrdinalIgnoreCase);

        var changed = false;

        foreach (var serverChat in snapshot.Values)
        {
            var key = GetConversationKey(serverChat, AuthState.UserId);
            var existing = _chats.FirstOrDefault(x =>
                string.Equals(GetConversationKey(x.Chat, AuthState.UserId), key, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var newItem = new ChatListItem
                {
                    Chat = serverChat,
                    DisplayName = serverChat.OtherUserName(AuthState.UserId),
                    LastMessage = serverChat.LastMessage == null ? "No messages yet." : FormatLastMessage(serverChat.LastMessage),
                    IsOnline = IsUserOnline(serverChat.OtherUserId(AuthState.UserId))
                };

                _chats.Add(newItem);
                _ = LoadAvatarForNewChatItemAsync(newItem);
                changed = true;
                continue;
            }

            var serverLastId = serverChat.LastMessage?.Id;
            var localLastId = existing.Chat.LastMessage?.Id;
            if (serverLastId != localLastId)
            {
                existing.Chat.LastMessage = serverChat.LastMessage;
                existing.LastMessage = serverChat.LastMessage == null ? "No messages yet." : FormatLastMessage(serverChat.LastMessage);
                changed = true;
            }

            var serverName = serverChat.OtherUserName(AuthState.UserId);
            if (!string.Equals(existing.DisplayName, serverName, StringComparison.Ordinal))
            {
                existing.DisplayName = serverName;
                changed = true;
            }

            var online = IsUserOnline(serverChat.OtherUserId(AuthState.UserId));
            if (existing.IsOnline != online)
            {
                existing.IsOnline = online;
                changed = true;
            }

            if (serverChat.IsGroup)
            {
                if (!string.Equals(existing.Chat.AvatarUrl, serverChat.AvatarUrl, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Chat.AvatarUrl = serverChat.AvatarUrl;
                    changed = true;
                }
            }
            else
            {
                existing.Chat.User1AvatarUrl = serverChat.User1AvatarUrl;
                existing.Chat.User2AvatarUrl = serverChat.User2AvatarUrl;
            }

            _ = RefreshAvatarOnlyWhenChangedAsync(existing, serverChat);
        }

        foreach (var duplicate in _chats
            .GroupBy(x => GetConversationKey(x.Chat, AuthState.UserId), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderBy(x => x.Chat.Id).Skip(1))
            .ToList())
        {
            _chats.Remove(duplicate);
            if (_currentChatId == duplicate.Chat.Id)
                ClearCurrentChatUi();
            changed = true;
        }

        foreach (var item in _chats.ToList())
        {
            var key = GetConversationKey(item.Chat, AuthState.UserId);
            if (snapshot.ContainsKey(key))
                continue;

            _chats.Remove(item);
            if (_currentChatId == item.Chat.Id)
                ClearCurrentChatUi();
            changed = true;
        }

        if (changed)
            RefreshChatsList();
    }

    private static string GetConversationKey(ChatModel chat, string currentUserId)
    {
        var otherUserId = chat.OtherUserId(currentUserId);
        if (string.IsNullOrWhiteSpace(otherUserId))
            return $"chat:{chat.Id}";

        return $"user:{otherUserId.Trim().ToUpperInvariant()}";
    }

    private async Task LoadAvatarForNewChatItemAsync(ChatListItem item)
    {
        string? endpoint;

        if (item.Chat.IsGroup)
        {
            endpoint = string.IsNullOrWhiteSpace(item.Chat.AvatarUrl)
                ? null
                : $"api/Chat/{item.Chat.Id}/avatar";
        }
        else
        {
            var otherUserId = item.Chat.OtherUserId(AuthState.UserId);
            endpoint = string.IsNullOrWhiteSpace(otherUserId)
                ? null
                : $"api/User/profile/{Uri.EscapeDataString(otherUserId)}/avatar";
        }

        var absolute = string.IsNullOrWhiteSpace(endpoint)
            ? null
            : _apiService.BuildAbsoluteUrl(endpoint);

        item.AvatarUri = absolute;
        if (string.IsNullOrWhiteSpace(absolute))
        {
            item.AvatarSource = null;
            return;
        }

        try
        {
            var image = await LoadConversationAvatarAsync(absolute);
            await Dispatcher.InvokeAsync(() => item.AvatarSource = image);
            if (IsLoaded)
                RefreshChatsList();
        }
        catch
        {
        }
    }

    private async Task RefreshAvatarOnlyWhenChangedAsync(ChatListItem item, ChatModel serverChat)
    {
        string? endpoint;

        if (serverChat.IsGroup)
        {
            endpoint = string.IsNullOrWhiteSpace(serverChat.AvatarUrl)
                ? null
                : $"api/Chat/{serverChat.Id}/avatar";
        }
        else
        {
            var avatarUrl = string.Equals(serverChat.User1Id, AuthState.UserId, StringComparison.OrdinalIgnoreCase)
                ? serverChat.User2AvatarUrl
                : serverChat.User1AvatarUrl;

            endpoint = string.IsNullOrWhiteSpace(avatarUrl)
                ? null
                : $"api/User/profile/{Uri.EscapeDataString(serverChat.OtherUserId(AuthState.UserId))}/avatar";
        }

        var absolute = string.IsNullOrWhiteSpace(endpoint)
            ? null
            : _apiService.BuildAbsoluteUrl(endpoint);

        if (string.Equals(item.AvatarUri, absolute, StringComparison.OrdinalIgnoreCase))
            return;

        item.AvatarUri = absolute;

        if (string.IsNullOrWhiteSpace(absolute))
        {
            item.AvatarSource = null;
            return;
        }

        try
        {
            var image = await LoadConversationAvatarAsync(absolute);
            await Dispatcher.InvokeAsync(() => item.AvatarSource = image);
        }
        catch
        {
        }
    }

    private void ClearCurrentChatUi()
    {
        if (!_currentChatId.HasValue)
            return;

        if (_chats.Any(x => x.Chat.Id == _currentChatId.Value))
            return;

        _currentChatId = null;
        _currentOtherUserId = string.Empty;
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;
        ChatUserNameText.Text = "Select a chat";
        ChatStatusText.Text = "Offline";
        ChatStatusIndicator.Fill = System.Windows.Media.Brushes.Gray;
        ChatHeaderAvatarImage.Source = null;
        ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
        ChatAvatarInitialsText.Visibility = Visibility.Visible;
        MessagesPanel.Children.Clear();
        MessageTextBox.Clear();
        UpdateLoadOlderButton();
    }
}
