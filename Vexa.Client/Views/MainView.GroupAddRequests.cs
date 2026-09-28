using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly GroupAddRequestUiBootstrap _groupAddRequestUiBootstrap = new();
    private static MainView? _activeMainViewForGroupRequests;
    private DispatcherTimer? _groupAddRequestTimer;
    private StackPanel? _groupAddRequestsPanel;
    private TextBlock? _groupAddRequestsHeader;
    private bool _groupAddRequestBusy;
    private bool _groupRequestEventsHooked;

    private sealed class GroupAddRequestUiBootstrap
    {
        private static bool _registered;
        public GroupAddRequestUiBootstrap()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnMainViewLoaded));
            EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnAddMemberButtonClicked));
        }

        private static async void OnMainViewLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainView view) return;
            _activeMainViewForGroupRequests = view;
            view.StartGroupAddRequestWatcher();
            await view.RefreshGroupAddRequestsAsync();
        }

        private static async void OnAddMemberButtonClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button button || !string.Equals(button.Content?.ToString(), "＋  Add member", StringComparison.Ordinal)) return;
            var view = _activeMainViewForGroupRequests;
            if (view == null) return;
            e.Handled = true;
            await view.OpenGroupAddMemberRequestFlowAsync();
        }
    }

    private void StartGroupAddRequestWatcher()
    {
        if (_groupAddRequestTimer != null) return;
        _groupAddRequestTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _groupAddRequestTimer.Tick += async (_, _) => await RefreshGroupAddRequestsAsync();
        _groupAddRequestTimer.Start();
    }

    private void HookGroupRequestSignalREvents()
    {
        if (_groupRequestEventsHooked || _hubConnection?.State != HubConnectionState.Connected) return;

        try
        {
            _hubConnection.On<GroupAddRequestClientModel>("GroupAddRequestAccepted", OnGroupAddRequestAccepted);
            _hubConnection.On<GroupAddRequestClientModel>("GroupAddRequestRejected", OnGroupAddRequestRejected);
            _groupRequestEventsHooked = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Group request SignalR hook failed: {ex}");
        }
    }

    private async void OnGroupAddRequestAccepted(GroupAddRequestClientModel request)
    {
        if (request == null) return;

        try
        {
            var operation = Dispatcher.InvokeAsync(async () =>
            {
                await RefreshGroupAddRequestsAsync();
                await LoadChatsAsync();
                MessageBox.Show(
                    $"@{request.TargetUsername} accepted the request and was added to '{request.GroupName}'.",
                    "Group Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            await operation.Task.Unwrap();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Group request accepted notification failed: {ex}");
        }
    }

    private async void OnGroupAddRequestRejected(GroupAddRequestClientModel request)
    {
        if (request == null) return;

        try
        {
            var operation = Dispatcher.InvokeAsync(async () =>
            {
                await RefreshGroupAddRequestsAsync();
                MessageBox.Show(
                    $"@{request.TargetUsername} rejected the request to join '{request.GroupName}'.",
                    "Group Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            await operation.Task.Unwrap();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Group request rejected notification failed: {ex}");
        }
    }

    private void EnsureGroupAddRequestsUi()
    {
        if (_groupAddRequestsPanel != null && _groupAddRequestsHeader != null) return;
        if (ChatsList.Parent is not StackPanel parent) return;

        var chatListIndex = parent.Children.IndexOf(ChatsList);
        if (chatListIndex < 0) return;

        _groupAddRequestsHeader = new TextBlock
        {
            Text = "GROUP REQUESTS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };

        _groupAddRequestsPanel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10)
        };

        parent.Children.Insert(chatListIndex, _groupAddRequestsHeader);
        parent.Children.Insert(chatListIndex + 1, _groupAddRequestsPanel);
    }

    private async Task RefreshGroupAddRequestsAsync()
    {
        if (!AuthState.IsAuthenticated || _groupAddRequestBusy) return;
        _groupAddRequestBusy = true;

        try
        {
            HookGroupRequestSignalREvents();
            await Dispatcher.InvokeAsync(EnsureGroupAddRequestsUi);

            var incoming = await _apiService.GetAsync<List<GroupAddRequestClientModel>>("api/GroupAddRequests/incoming") ?? [];
            var outgoing = await _apiService.GetAsync<List<GroupAddRequestClientModel>>("api/GroupAddRequests/outgoing") ?? [];

            await Dispatcher.InvokeAsync(() => RenderGroupAddRequests(incoming, outgoing));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Group add request refresh failed: {ex.Message}");
        }
        finally
        {
            _groupAddRequestBusy = false;
        }
    }

    private void RenderGroupAddRequests(List<GroupAddRequestClientModel> incoming, List<GroupAddRequestClientModel> outgoing)
    {
        EnsureGroupAddRequestsUi();
        if (_groupAddRequestsPanel == null || _groupAddRequestsHeader == null) return;

        _groupAddRequestsPanel.Children.Clear();
        var visible = incoming.Count > 0 || outgoing.Count > 0;
        _groupAddRequestsHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _groupAddRequestsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        foreach (var request in incoming)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = request.GroupName, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
            content.Children.Add(new TextBlock { Text = $"@{request.RequesterUsername} wants to add you to this group.", FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var reject = new Button { Content = "Reject", Height = 28, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
            var accept = new Button { Content = "Accept", Height = 28, Padding = new Thickness(9, 0, 9, 0), Style = (Style)FindResource("PrimaryButtonStyle") };
            reject.Click += async (_, _) => await RespondToGroupAddRequestAsync(request, false);
            accept.Click += async (_, _) => await RespondToGroupAddRequestAsync(request, true);
            actions.Children.Add(reject);
            actions.Children.Add(accept);
            content.Children.Add(actions);

            _groupAddRequestsPanel.Children.Add(new Border
            {
                Padding = new Thickness(10),
                Background = (Brush)FindResource("InputBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 0, 7),
                Child = content
            });
        }

        foreach (var request in outgoing)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = request.GroupName, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
            content.Children.Add(new TextBlock { Text = $"Waiting for @{request.TargetUsername} to accept the group request.", FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });

            _groupAddRequestsPanel.Children.Add(new Border
            {
                Padding = new Thickness(10),
                Background = (Brush)FindResource("InputBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 0, 7),
                Child = content
            });
        }
    }

    private async Task OpenGroupAddMemberRequestFlowAsync()
    {
        if (!AuthState.IsAuthenticated || !_currentChatId.HasValue || !IsCurrentGroupChat) return;
        if (Window.GetWindow(this) is not Window owner) return;

        var username = PromptText("Add Member", "Username", string.Empty, owner);
        if (string.IsNullOrWhiteSpace(username)) return;

        try
        {
            var normalized = username.Trim().ToLowerInvariant();
            var candidates = await _apiService.GetAsync<List<GroupAddCandidate>>($"api/User/search?q={Uri.EscapeDataString(normalized)}") ?? [];
            var candidate = candidates.FirstOrDefault(x => string.Equals(x.Username, normalized, StringComparison.OrdinalIgnoreCase));
            if (candidate == null)
            {
                MessageBox.Show($"User @{normalized} was not found.", "Add Member", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = await _apiService.PostAsync<object, GroupAddRequestActionResponse>($"api/GroupAddRequests/{_currentChatId.Value}", new { Username = candidate.Username });
            if (result == null) return;

            var message = result.RequestPending
                ? $"@{candidate.Username} does not allow people to add them to groups.\n\nA request was sent. They must accept it before they are added to the group."
                : result.Message;

            MessageBox.Show(message, "Add Member", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshGroupAddRequestsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add the member.\n\n{ex.Message}", "Add Member", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RespondToGroupAddRequestAsync(GroupAddRequestClientModel request, bool accept)
    {
        try
        {
            var endpoint = $"api/GroupAddRequests/{request.Id}/{(accept ? "accept" : "reject")}";
            var result = await _apiService.PostAsync<object, GroupAddRequestActionResponse>(endpoint, new { });
            if (result == null) return;

            await RefreshGroupAddRequestsAsync();
            if (accept)
            {
                await LoadChatsAsync();
                MessageBox.Show($"You were added to '{request.GroupName}'.", "Group Request", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Group Request", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed class GroupAddRequestClientModel
    {
        public long Id { get; set; }
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string RequesterUserId { get; set; } = string.Empty;
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
        public string TargetUserId { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
    }

    private sealed class GroupAddRequestActionResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool RequestPending { get; set; }
    }
}
