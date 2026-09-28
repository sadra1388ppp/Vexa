using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Services;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaChat.Client.Models;
using Microsoft.Win32;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private sealed class GroupMemberModel { public string UserId { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string Role { get; set; } = string.Empty; }
    private sealed class GroupUserSearchModel { public string Id { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public string? AvatarUrl { get; set; } public bool IsOnline { get; set; } }
    private sealed class GroupCreateResponse { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; } public List<string> SkippedUsernames { get; set; } = []; }
    private sealed class GroupAddRequestResponse { public string Message { get; set; } = string.Empty; public bool RequestPending { get; set; } }
    private static bool _groupUiRegistered;
    private Button? _createGroupButton;
    private DispatcherTimer? _groupEventTimer;
    private bool _groupEventsHooked;
    private bool _groupCreationBusy;
    private List<GroupMemberModel> _currentGroupMembers = [];
    private bool IsCurrentGroupChat => _currentChatId.HasValue && _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value)?.Chat.IsGroup == true;
    private void RefreshGroupOnlineStatus() { if (!IsCurrentGroupChat || !_currentChatId.HasValue) return; _ = RefreshCurrentGroupInfoAsync(); }
    private void UpdateGroupOnlineStatusFromCache() { if (!IsCurrentGroupChat) return; var online = _currentGroupMembers.Count(member => _onlineUserIds.Contains(member.UserId)); ChatStatusText.Text = $"{online} member{(online == 1 ? "" : "s")} online"; ChatStatusIndicator.Fill = Brushes.LimeGreen; if (online <= 0) ChatStatusIndicator.Fill = Brushes.Gray; }
    private async Task RefreshCurrentGroupInfoAsync() { if (!_currentChatId.HasValue || !IsCurrentGroupChat) return; try { _currentGroupMembers = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{_currentChatId.Value}/members") ?? []; UpdateGroupOnlineStatusFromCache(); } catch { } }
    private async Task RefreshCurrentGroupAvatarAsync()
    {
        if (!_currentChatId.HasValue || !IsCurrentGroupChat) return;
        var item = _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value);
        if (item == null) return;
        if (string.IsNullOrWhiteSpace(item.Chat.AvatarUrl)) { ChatHeaderAvatarImage.Source = null; ChatHeaderAvatarImage.Visibility = Visibility.Collapsed; ChatAvatarInitialsText.Visibility = Visibility.Visible; return; }
        try
        {
            var endpoint = $"api/Chat/{item.Chat.Id}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(endpoint));
            if (image == null) return;
            await Dispatcher.InvokeAsync(() => { ChatHeaderAvatarImage.Source = image; ChatHeaderAvatarImage.Visibility = Visibility.Visible; ChatAvatarInitialsText.Visibility = Visibility.Collapsed; });
        }
        catch { }
    }
    private void ChatHeaderGroupInfo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (!IsCurrentGroupChat) return; e.Handled = true; OpenGroupInfo(); }
    private async void OpenGroupInfo()
    {
        if (!IsCurrentGroupChat || !_currentChatId.HasValue) return;
        try
        {
            var chatId = _currentChatId.Value;
            var members = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{chatId}/members") ?? [];
            _currentGroupMembers = members;
            var online = members.Count(member => _onlineUserIds.Contains(member.UserId));
            var currentMember = members.FirstOrDefault(m => string.Equals(m.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            var canEdit = string.Equals(currentMember?.Role, "Owner", StringComparison.OrdinalIgnoreCase) || string.Equals(currentMember?.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            var chat = _chats.FirstOrDefault(x => x.Chat.Id == chatId)?.Chat;
            var dialog = new Window { Title = "Group Info", Width = 460, Height = 700, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("PanelBackgroundBrush") };
            var panel = new StackPanel { Margin = new Thickness(22) };
            var avatarGrid = new Grid { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            avatarGrid.Children.Add(new Border { CornerRadius = new CornerRadius(48), Background = (Brush)FindResource("PrimarySoftBrush") });
            avatarGrid.Children.Add(new TextBlock { Text = BuildGroupInitials(chat?.Name), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrWhiteSpace(chat?.AvatarUrl)) { var groupEndpoint = _apiService.BuildAbsoluteUrl($"api/Chat/{chatId}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"); var img = new Image { Width = 96, Height = 96, Stretch = Stretch.UniformToFill, Source = await LoadConversationAvatarAsync(groupEndpoint) }; img.Clip = new EllipseGeometry(new Point(48, 48), 48, 48); avatarGrid.Children.Add(img); }
            panel.Children.Add(avatarGrid);
            panel.Children.Add(new TextBlock { Text = chat?.Name ?? ChatUserNameText.Text, FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") });
            panel.Children.Add(new TextBlock { Text = $"{online} member{(online == 1 ? "" : "s")} online • {members.Count} members", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 14), Foreground = (Brush)FindResource("SecondaryTextBrush") });
            if (canEdit)
            {
                var avatarButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
                var changeButton = new Button { Content = "Change picture", Height = 34, Padding = new Thickness(12, 0, 12, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
                changeButton.Click += async (_, _) => { var picker = new OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp", Title = "Choose group picture" }; if (picker.ShowDialog(dialog) != true) return; try { var result = await _apiService.UploadFileAsync<GroupAvatarResponse>($"api/Chat/{chatId}/avatar", picker.FileName); if (result?.Chat == null) throw new InvalidOperationException("The server did not return the updated group."); var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId); if (item != null) { item.Chat.AvatarUrl = result.Chat.AvatarUrl; item.AvatarUri = _apiService.BuildAbsoluteUrl($"api/Chat/{chatId}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"); item.AvatarSource = await LoadConversationAvatarAsync(item.AvatarUri); } await RefreshCurrentGroupAvatarAsync(); dialog.Close(); OpenGroupInfo(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Group Picture", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                avatarButtons.Children.Add(changeButton);
                var removeButton = new Button { Content = "Remove", Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("DangerButtonStyle") };
                removeButton.Click += async (_, _) => { try { var ok = await _apiService.DeleteAsync($"api/Chat/{chatId}/avatar"); if (ok) { var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId); if (item != null) { item.Chat.AvatarUrl = null; item.AvatarUri = null; item.AvatarSource = null; } await RefreshCurrentGroupAvatarAsync(); RefreshChatsList(); dialog.Close(); OpenGroupInfo(); } } catch (Exception ex) { MessageBox.Show(ex.Message, "Group Picture", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                avatarButtons.Children.Add(removeButton); panel.Children.Add(avatarButtons);
            }
            panel.Children.Add(new TextBlock { Text = "MEMBERS", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 8) });
            var list = new ListBox { Height = 390, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            foreach (var member in members) { var isOnline = _onlineUserIds.Contains(member.UserId); var role = string.IsNullOrWhiteSpace(member.Role) ? "Member" : member.Role; list.Items.Add(new TextBlock { Text = $"{(isOnline ? "●" : "○")}  {member.DisplayName}  •  {role}  •  {(isOnline ? "Online" : "Offline")}", FontSize = 15, Margin = new Thickness(6, 8, 6, 8), Foreground = (Brush)FindResource("TextBrush") }); }
            panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load group information.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static string BuildGroupInitials(string? name) { if (string.IsNullOrWhiteSpace(name)) return "G"; var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); return parts.Length > 1 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : name.Trim()[..Math.Min(2, name.Trim().Length)].ToUpperInvariant(); }
    private sealed class GroupAvatarResponse { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; } }
    private static void OnGroupUiLoaded(object sender, RoutedEventArgs e) { if (sender is not MainView view) return; view.InstallGroupUi(); view.StartGroupEventWatcher(); }
    private static void RegisterGroupUiHandlers() { if (_groupUiRegistered) return; _groupUiRegistered = true; EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnGroupUiLoaded)); }
    private void InstallGroupUi() { if (_createGroupButton != null) return; if (SearchTextBox.Parent is not Grid searchGrid || searchGrid.Parent is not Border searchBorder || searchBorder.Parent is not StackPanel panel) return; _createGroupButton = new Button { Content = "👥   New group", Height = 40, Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(10, 0, 10, 0), Style = (Style)FindResource("SecondaryButtonStyle") }; _createGroupButton.Click += CreateGroupButton_Click; panel.Children.Add(_createGroupButton); }
    private void StartGroupEventWatcher() { if (_groupEventTimer != null) return; _groupEventTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _groupEventTimer.Tick += GroupEventTimer_Tick; _groupEventTimer.Start(); }
    private async void GroupEventTimer_Tick(object? sender, EventArgs e) { if (_groupCreationBusy) return; if (_hubConnection?.State != HubConnectionState.Connected) return; if (!_groupEventsHooked) { try { _hubConnection.On<ChatModel>("ChatCreated", OnGroupCreatedFromServer); _hubConnection.On<ChatModel>("GroupUpdated", OnGroupUpdatedFromServer); _hubConnection.On<object>("ChatMemberRemoved", OnGroupMemberRemovedFromServer); _groupEventsHooked = true; } catch { } } RefreshGroupOnlineStatus(); }
    private async void OnGroupCreatedFromServer(ChatModel chat) { if (_groupCreationBusy || chat == null || chat.Id <= 0) return; await Dispatcher.InvokeAsync(async () => { if (_groupCreationBusy) return; try { await LoadChatsAsync(); } catch { } }); }
    private async void OnGroupUpdatedFromServer(ChatModel chat) { if (_groupCreationBusy || chat == null || chat.Id <= 0) return; await Dispatcher.InvokeAsync(async () => { if (_groupCreationBusy) return; try { var item = _chats.FirstOrDefault(x => x.Chat.Id == chat.Id); if (item != null) item.Chat.AvatarUrl = chat.AvatarUrl; await LoadChatsAsync(); if (_currentChatId == chat.Id) await RefreshCurrentGroupAvatarAsync(); } catch { } }); }
    private async void OnGroupMemberRemovedFromServer(object _) { if (_groupCreationBusy) return; await Dispatcher.InvokeAsync(async () => { if (_groupCreationBusy) return; try { await LoadChatsAsync(); } catch { } }); }

    private async void CreateGroupButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_groupCreationBusy) return;
        var dialog = new Window
        {
            Title = "Create New Group",
            Width = 700,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush"),
            WindowStyle = WindowStyle.SingleBorderWindow
        };

        var selectedUsers = new Dictionary<string, GroupUserSearchModel>(StringComparer.OrdinalIgnoreCase);
        var root = new Grid { Margin = new Thickness(26) };
        for (var i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions[6].Height = new GridLength(1, GridUnitType.Star);

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var headerBadge = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(15), Background = (Brush)FindResource("PrimarySoftBrush"), HorizontalAlignment = HorizontalAlignment.Left };
        headerBadge.Child = new TextBlock { Text = "👥", FontSize = 23, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        headerGrid.Children.Add(headerBadge);
        var headerText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "Create a group", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") };
        var subtitle = new TextBlock { Text = "Choose a name and invite the people you want in the conversation.", FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("SecondaryTextBrush") };
        headerText.Children.Add(title); headerText.Children.Add(subtitle); Grid.SetColumn(headerText, 1); headerGrid.Children.Add(headerText);
        Grid.SetRow(headerGrid, 0); root.Children.Add(headerGrid);

        var nameLabel = new TextBlock { Text = "GROUP NAME", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 7) };
        Grid.SetRow(nameLabel, 1); root.Children.Add(nameLabel);
        var nameBox = new TextBox { Height = 44, Padding = new Thickness(13, 0, 13, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14, ToolTip = "Enter a group name" };
        Grid.SetRow(nameBox, 2); root.Children.Add(nameBox);

        var selectedHeader = new Grid { Margin = new Thickness(0, 17, 0, 8) };
        var selectedLabel = new TextBlock { Text = "SELECTED MEMBERS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("SecondaryTextBrush") };
        var selectedCount = new TextBlock { Text = "0 selected", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Right };
        selectedHeader.Children.Add(selectedLabel); selectedHeader.Children.Add(selectedCount); Grid.SetRow(selectedHeader, 3); root.Children.Add(selectedHeader);

        var selectedScroll = new ScrollViewer { Height = 62, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = (Brush)FindResource("InputBackgroundBrush") };
        var selectedPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7, 8, 7) };
        selectedScroll.Content = selectedPanel;
        Grid.SetRow(selectedScroll, 4); root.Children.Add(selectedScroll);

        var searchPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 10) };
        var searchLabel = new TextBlock { Text = "ADD MEMBERS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 7) };
        var searchGrid = new Grid { Height = 44 };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var membersList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
        ScrollViewer.SetVerticalScrollBarVisibility(membersList, ScrollBarVisibility.Auto);
        var loadingText = new TextBlock { Text = "", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var searchBox = new TextBox { Height = 44, Padding = new Thickness(40, 0, 44, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13.5, Foreground = (Brush)FindResource("TextBrush"), Background = (Brush)FindResource("InputBackgroundBrush"), ToolTip = "Search by username or display name" };
        searchBox.TextChanged += async (_, _) => await RefreshGroupUserSearchAsync(searchBox.Text, membersList, selectedUsers, loadingText);
        var searchIcon = new TextBlock { Text = "⌕", FontSize = 23, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 1) };
        var searchHost = new Grid(); searchHost.Children.Add(searchBox); searchHost.Children.Add(searchIcon); Grid.SetColumn(searchHost, 0); searchGrid.Children.Add(searchHost);
        var clearSearch = new Button { Content = "Clear", Height = 36, Margin = new Thickness(9, 4, 0, 4), Padding = new Thickness(13, 0, 13, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        clearSearch.Click += (_, _) => searchBox.Clear(); Grid.SetColumn(clearSearch, 1); searchGrid.Children.Add(clearSearch);
        searchPanel.Children.Add(searchLabel); searchPanel.Children.Add(searchGrid); Grid.SetRow(searchPanel, 5); root.Children.Add(searchPanel);

        var resultHost = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        resultHost.Children.Add(membersList); resultHost.Children.Add(loadingText);
        Grid.SetRow(resultHost, 6); root.Children.Add(resultHost);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerHint = new TextBlock { Text = "Members who have group privacy enabled may receive an invitation request.", TextWrapping = TextWrapping.Wrap, FontSize = 11.5, MaxWidth = 390, Foreground = (Brush)FindResource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
        var createButton = new Button { Content = "Create Group", Width = 150, Height = 44, Margin = new Thickness(14, 0, 0, 0), Style = (Style)FindResource("PrimaryButtonStyle") };
        footer.Children.Add(footerHint); Grid.SetColumn(createButton, 1); footer.Children.Add(createButton); Grid.SetRow(footer, 7); root.Children.Add(footer);

        void UpdateSelectedMembersUi()
        {
            selectedPanel.Children.Clear();
            foreach (var user in selectedUsers.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var chip = new Border { Background = (Brush)FindResource("PrimarySoftBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(2), Padding = new Thickness(8, 4, 6, 4) };
                var chipGrid = new Grid(); chipGrid.ColumnDefinitions.Add(new ColumnDefinition()); chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new TextBlock { Text = $"@{user.Username}", FontSize = 11.5, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 7, 0) };
                var remove = new Button { Content = "×", Width = 22, Height = 22, FontSize = 14, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = (Brush)FindResource("SecondaryTextBrush") };
                remove.Click += (_, _) => { selectedUsers.Remove(user.Username); UpdateSelectedMembersUi(); _ = RefreshGroupUserSearchAsync(searchBox.Text, membersList, selectedUsers, loadingText); };
                chipGrid.Children.Add(text); Grid.SetColumn(remove, 1); chipGrid.Children.Add(remove); chip.Child = chipGrid; selectedPanel.Children.Add(chip);
            }
            selectedCount.Text = $"{selectedUsers.Count} selected";
            createButton.IsEnabled = !_groupCreationBusy && selectedUsers.Count > 0 && !string.IsNullOrWhiteSpace(nameBox.Text.Trim());
        }

        nameBox.TextChanged += (_, _) => UpdateSelectedMembersUi();
        createButton.Click += async (_, _) =>
        {
            if (_groupCreationBusy) return;
            var name = nameBox.Text.Trim();
            var selected = selectedUsers.Values.Select(x => x.Username).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Please enter a group name.", "Create Group", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (selected.Count == 0) { MessageBox.Show("Select at least one member.", "Create Group", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _groupCreationBusy = true;
            createButton.IsEnabled = false;
            createButton.Content = "Creating…";
            try
            {
                var response = await _apiService.PostAsync<GroupCreateResponse>("api/Chat/group", new { Name = name, Usernames = selected });
                if (response == null) throw new InvalidOperationException("The server returned an empty response.");
                var created = response.Chat;
                if (created == null || created.Id <= 0) throw new InvalidOperationException(response.Message ?? "The server did not return the created group.");
                var requestFailures = new List<string>();
                foreach (var username in response.SkippedUsernames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var request = await _apiService.PostAsync<object, GroupAddRequestResponse>($"api/GroupAddRequests/{created.Id}", new { Username = username });
                        if (request == null || !request.RequestPending) requestFailures.Add(username);
                    }
                    catch { requestFailures.Add(username); }
                }
                await LoadChatsAsync();
                dialog.Close();
                if (response.SkippedUsernames.Count > 0)
                {
                    var requested = response.SkippedUsernames.Except(requestFailures, StringComparer.OrdinalIgnoreCase).Select(x => "@" + x).ToList();
                    var message = requested.Count > 0
                        ? $"The group was created. These users do not allow direct group additions, so a request was sent to: {string.Join(", ", requested)}. They must accept before joining."
                        : "The group was created, but the selected users could not be reached for group requests.";
                    MessageBox.Show(message, "Group privacy", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create group.\n\n{ex.Message}", "Create Group", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _groupCreationBusy = false;
                createButton.Content = "Create Group";
                if (dialog.IsVisible) UpdateSelectedMembersUi();
            }
        };
        UpdateSelectedMembersUi();
        searchBox.Focus();
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private async Task RefreshGroupUserSearchAsync(string query, ListBox membersList, Dictionary<string, GroupUserSearchModel> selectedUsers, TextBlock loadingText)
    {
        var trimmedQuery = query.Trim();
        try
        {
            loadingText.Text = string.IsNullOrWhiteSpace(trimmedQuery) ? "Type a name or username to search" : "Searching…";
            loadingText.Visibility = Visibility.Visible;
            membersList.Items.Clear();
            var users = await _apiService.GetAsync<List<GroupUserSearchModel>>($"api/User/search?q={Uri.EscapeDataString(trimmedQuery)}") ?? [];
            var visibleUsers = users.Where(x => !string.Equals(x.Username, AuthState.Username, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var user in visibleUsers)
            {
                var isSelected = selectedUsers.ContainsKey(user.Username);
                var check = new CheckBox { IsChecked = isSelected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), Tag = user };
                check.Checked += (_, _) => { selectedUsers[user.Username] = user; loadingText.Visibility = Visibility.Collapsed; UpdateGroupSelectionVisuals(membersList, selectedUsers); };
                check.Unchecked += (_, _) => { selectedUsers.Remove(user.Username); UpdateGroupSelectionVisuals(membersList, selectedUsers); };
                var avatar = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = (Brush)FindResource("PrimarySoftBrush"), VerticalAlignment = VerticalAlignment.Center };
                avatar.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(user.DisplayName) ? "?" : user.DisplayName[..1].ToUpperInvariant(), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var nameStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var display = new TextBlock { Text = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") };
                var handle = new TextBlock { Text = $"@{user.Username}{(user.IsOnline ? "  •  Online" : "")}", FontSize = 11.5, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 0) };
                nameStack.Children.Add(display); nameStack.Children.Add(handle);
                var row = new Grid { Height = 56, Margin = new Thickness(2, 3, 2, 3), Background = Brushes.Transparent };
                row.Children.Add(check); Grid.SetColumn(check, 0);
                row.Children.Add(avatar); Grid.SetColumn(avatar, 1);
                row.Children.Add(nameStack); Grid.SetColumn(nameStack, 2);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.MouseLeftButtonUp += (_, _) => check.IsChecked = !check.IsChecked;
                membersList.Items.Add(row);
            }
            loadingText.Visibility = visibleUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (visibleUsers.Count == 0 && !string.IsNullOrWhiteSpace(trimmedQuery)) loadingText.Text = "No users found";
        }
        catch
        {
            loadingText.Text = "Could not search users";
            loadingText.Visibility = Visibility.Visible;
        }
    }

    private static void UpdateGroupSelectionVisuals(ListBox membersList, Dictionary<string, GroupUserSearchModel> selectedUsers)
    {
        foreach (var row in membersList.Items.OfType<Grid>())
        {
            var check = row.Children.OfType<CheckBox>().FirstOrDefault();
            if (check?.Tag is GroupUserSearchModel user) check.IsChecked = selectedUsers.ContainsKey(user.Username);
        }
    }
}
