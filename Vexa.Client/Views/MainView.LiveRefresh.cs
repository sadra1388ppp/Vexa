using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private DispatcherTimer? _liveRefreshTimer;
    private bool _liveRefreshBusy;

    internal static void RegisterLiveRefresh()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(LiveRefreshLoaded));
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.UnloadedEvent, new RoutedEventHandler(LiveRefreshUnloaded));
    }

    private static void LiveRefreshLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.StartLiveRefresh();
    }

    private static void LiveRefreshUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.StopLiveRefresh();
    }

    private void StartLiveRefresh()
    {
        if (_liveRefreshTimer != null) return;

        _liveRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _liveRefreshTimer.Tick += LiveRefreshTimer_Tick;
        _liveRefreshTimer.Start();
        _ = LiveRefreshOnceAsync();
    }

    private void StopLiveRefresh()
    {
        if (_liveRefreshTimer == null) return;
        _liveRefreshTimer.Stop();
        _liveRefreshTimer.Tick -= LiveRefreshTimer_Tick;
        _liveRefreshTimer = null;
    }

    private async void LiveRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await LiveRefreshOnceAsync();
    }

    private async Task LiveRefreshOnceAsync()
    {
        if (_liveRefreshBusy || _isCreatingChatSafely || !AuthState.IsAuthenticated)
            return;

        _liveRefreshBusy = true;
        try
        {
            Task<List<string>>? onlineTask = null;
            if (_hubConnection?.State == HubConnectionState.Connected)
                onlineTask = _hubConnection.InvokeAsync<List<string>>("GetOnlineUsers");

            var chatsTask = _apiService.GetAsync<List<ChatModel>>("api/Chat");

            if (onlineTask != null)
                await Task.WhenAll(onlineTask, chatsTask);
            else
                await chatsTask;

            var onlineUsers = onlineTask?.Status == TaskStatus.RanToCompletion
                ? await onlineTask
                : null;
            var serverChats = chatsTask.Status == TaskStatus.RanToCompletion
                ? await chatsTask
                : null;

            if (_isCreatingChatSafely)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (_isCreatingChatSafely) return;
                ApplyOnlineUsers(onlineUsers);
                ApplyChatSnapshot(serverChats);

                // Group presence is calculated from the group's member list.
                // Private chats continue to use the normal single-user presence.
                if (IsCurrentGroupChat)
                    UpdateGroupOnlineStatusFromCache();
                else
                    UpdateCurrentChatPresence();
            }, DispatcherPriority.Background);
        }
        catch
        {
        }
        finally
        {
            _liveRefreshBusy = false;
        }
    }

    private void ApplyOnlineUsers(IEnumerable<string>? ids)
    {
        if (ids == null) return;

        var next = ids.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_onlineUserIds.SetEquals(next))
        {
            foreach (var item in _chats)
            {
                var online = IsUserOnline(item.Chat.OtherUserId(AuthState.UserId));
                if (item.IsOnline != online) item.IsOnline = online;
            }
            if (IsCurrentGroupChat)
                UpdateGroupOnlineStatusFromCache();
            else
                UpdateCurrentChatPresence();
            return;
        }

        _onlineUserIds.Clear();
        foreach (var id in next)
            _onlineUserIds.Add(id);

        foreach (var item in _chats)
            item.IsOnline = IsUserOnline(item.Chat.OtherUserId(AuthState.UserId));
    }
}
