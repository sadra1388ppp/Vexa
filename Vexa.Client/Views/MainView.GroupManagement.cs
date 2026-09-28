using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async void ChatHeaderGroupInfoProfessional_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsCurrentGroupChat) { ChatHeaderProfile_Click(sender, e); return; }
        await OpenProfessionalGroupInfoAsync();
    }

    private async Task OpenProfessionalGroupInfoAsync()
    {
        if (!_currentChatId.HasValue || !IsCurrentGroupChat) return;
        try
        {
            var chatId = _currentChatId.Value;
            var chat = _chats.FirstOrDefault(x => x.Chat.Id == chatId)?.Chat;
            if (chat == null) return;
            var members = await GetGroupMembersAsync(chatId);
            var currentMember = members.FirstOrDefault(m => string.Equals(m.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            var isOwner = string.Equals(currentMember?.Role, "Owner", StringComparison.OrdinalIgnoreCase);
            var canManage = isOwner || string.Equals(currentMember?.Role, "Admin", StringComparison.OrdinalIgnoreCase);

            var dialog = new Window { Title = $"{chat.Name} • Group Info", Width = 560, Height = 760, MinWidth = 500, MinHeight = 650, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = GetBrush("AppBackgroundBrush"), ResizeMode = ResizeMode.CanResize };
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var avatar = await BuildGroupAvatarAsync(chat); Grid.SetColumn(avatar, 0); header.Children.Add(avatar);
            var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock { Text = chat.Name, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = GetBrush("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
            var memberCountText = new TextBlock { Text = MemberCountText(members.Count), FontSize = 13, Margin = new Thickness(0, 5, 0, 0), Foreground = GetBrush("SecondaryTextBrush") };
            titlePanel.Children.Add(titleText); titlePanel.Children.Add(memberCountText); Grid.SetColumn(titlePanel, 1); header.Children.Add(titlePanel);
            var closeButton = new Button { Content = "×", Width = 40, Height = 40, Style = (Style)FindResource("IconButtonStyle"), ToolTip = "Close" }; closeButton.Click += (_, _) => dialog.Close(); Grid.SetColumn(closeButton, 2); header.Children.Add(closeButton); Grid.SetRow(header, 0); root.Children.Add(header);

            var memberStack = new StackPanel();
            var state = new GroupInfoDialogState(dialog, chat, avatar, titleText, memberCountText, memberStack, isOwner, canManage, chatId);
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            if (canManage)
            {
                var rename = CreateActionButton("✎  Rename", "SecondaryButtonStyle"); rename.Click += async (_, _) => await RenameGroupAsync(state); actionPanel.Children.Add(rename);
                var changePicture = CreateActionButton("▣  Picture", "SecondaryButtonStyle"); changePicture.Click += async (_, _) => await ChangeGroupPictureAsync(state); actionPanel.Children.Add(changePicture);
                var addMember = CreateActionButton("＋  Add member", "PrimaryButtonStyle"); addMember.Click += async (_, _) => await AddGroupMemberAsync(state); actionPanel.Children.Add(addMember);
            }
            else
            {
                var viewOnly = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(10), Background = GetBrush("InputBackgroundBrush") }; viewOnly.Child = new TextBlock { Text = "You are a member", FontSize = 12, Foreground = GetBrush("SecondaryTextBrush") }; actionPanel.Children.Add(viewOnly);
            }
            Grid.SetRow(actionPanel, 1); root.Children.Add(actionPanel);

            var membersPanel = new DockPanel(); var membersTitle = new TextBlock { Text = "MEMBERS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = GetBrush("SecondaryTextBrush"), Margin = new Thickness(2, 0, 0, 9) }; DockPanel.SetDock(membersTitle, Dock.Top); membersPanel.Children.Add(membersTitle);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = memberStack }; membersPanel.Children.Add(scroll); Grid.SetRow(membersPanel, 2); root.Children.Add(membersPanel);
            await RefreshGroupMembersAsync(state, members);

            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (isOwner)
            {
                var delete = CreateActionButton("Delete group", "DangerButtonStyle"); delete.Height = 42; delete.Click += async (_, _) => { if (MessageBox.Show($"Delete '{chat.Name}' permanently?\n\nAll messages and members will be removed for everyone. This cannot be undone.", "Delete Group", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return; try { await _apiService.DeleteAsync($"api/GroupManagement/{chatId}"); dialog.Close(); await RemoveChatFromLocalUiAsync(chatId); } catch (Exception ex) { MessageBox.Show(ex.Message, "Delete Group", MessageBoxButton.OK, MessageBoxImage.Error); } }; Grid.SetColumn(delete, 0); footer.Children.Add(delete);
            }
            else
            {
                var leave = CreateActionButton("Leave group", "DangerButtonStyle"); leave.Height = 42; leave.Click += async (_, _) => { if (MessageBox.Show($"Leave '{chat.Name}'?\n\nYou can be added again by a group administrator.", "Leave Group", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return; try { await _apiService.PostAsync<object, object>($"api/GroupManagement/{chatId}/leave", new { }); dialog.Close(); await RemoveChatFromLocalUiAsync(chatId); } catch (Exception ex) { MessageBox.Show(ex.Message, "Leave Group", MessageBoxButton.OK, MessageBoxImage.Warning); } }; Grid.SetColumn(leave, 0); footer.Children.Add(leave);
            }
            var done = CreateActionButton("Done", "SecondaryButtonStyle"); done.Width = 92; done.Height = 42; done.Click += (_, _) => dialog.Close(); Grid.SetColumn(done, 1); footer.Children.Add(done); Grid.SetRow(footer, 3); root.Children.Add(footer);
            dialog.Content = root; dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load group information.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task RenameGroupAsync(GroupInfoDialogState state)
    {
        var name = PromptText("Rename Group", "Group name", state.Chat.Name, state.Dialog);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), state.Chat.Name, StringComparison.Ordinal)) return;
        try
        {
            var newName = name.Trim(); await _apiService.PutAsync<object, object>($"api/Chat/{state.ChatId}/name", new { Name = newName });
            state.Chat.Name = newName; state.Dialog.Title = $"{newName} • Group Info"; state.TitleText.Text = newName;
            await LoadChatsAsync(); var refreshed = _chats.FirstOrDefault(x => x.Chat.Id == state.ChatId)?.Chat; if (refreshed != null) state.Chat = refreshed;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Rename Group", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task RefreshGroupMembersAsync(GroupInfoDialogState state, List<GroupMemberViewModel>? members = null)
    {
        members ??= await GetGroupMembersAsync(state.ChatId); state.MemberStack.Children.Clear();
        foreach (var member in members) state.MemberStack.Children.Add(await BuildMemberCardAsync(member, state.IsOwner, state.CanManage, state.ChatId, state));
        state.MemberCountText.Text = MemberCountText(members.Count);
    }

    private async Task<List<GroupMemberViewModel>> GetGroupMembersAsync(int chatId) => await _apiService.GetAsync<List<GroupMemberViewModel>>($"api/Chat/{chatId}/members") ?? [];
    private static string MemberCountText(int count) => $"{count} member{(count == 1 ? "" : "s")}";

    private async Task<Border> BuildMemberCardAsync(GroupMemberViewModel member, bool isOwner, bool canManage, int chatId, GroupInfoDialogState state)
    {
        var card = new Border { Background = GetBrush("InputBackgroundBrush"), BorderBrush = GetBrush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = new Grid { Width = 46, Height = 46, Margin = new Thickness(0, 0, 12, 0) }; avatar.Children.Add(new Border { CornerRadius = new CornerRadius(23), Background = GetBrush("PrimarySoftBrush") }); avatar.Children.Add(new TextBlock { Text = BuildInitials(member.DisplayName), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(member.AvatarUrl)) { try { var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(member.AvatarUrl)); if (image != null) { var imageControl = new Image { Width = 46, Height = 46, Stretch = Stretch.UniformToFill, Source = image, IsHitTestVisible = false }; imageControl.Clip = new EllipseGeometry(new Point(23, 23), 23, 23); avatar.Children.Add(imageControl); } } catch { } }
        Grid.SetColumn(avatar, 0); grid.Children.Add(avatar);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; info.Children.Add(new TextBlock { Text = member.DisplayName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = GetBrush("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis }); var subtitle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) }; subtitle.Children.Add(new TextBlock { Text = "@" + member.Username, FontSize = 11, Foreground = GetBrush("SecondaryTextBrush") }); subtitle.Children.Add(CreateRoleBadge(member.Role)); info.Children.Add(subtitle); Grid.SetColumn(info, 1); grid.Children.Add(info);
        if (canManage && !string.Equals(member.Role, "Owner", StringComparison.OrdinalIgnoreCase) && !string.Equals(member.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase))
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isOwner) { var admin = CreateActionButton(string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Remove admin" : "Make admin", "SecondaryButtonStyle"); admin.Height = 34; admin.Padding = new Thickness(10, 0, 10, 0); admin.Click += async (_, _) => await ChangeMemberRoleAsync(chatId, member, !string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase), state); controls.Children.Add(admin); }
            if (!string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase) || isOwner) { var remove = CreateActionButton("Remove", "DangerButtonStyle"); remove.Height = 34; remove.Padding = new Thickness(10, 0, 10, 0); remove.Margin = new Thickness(7, 0, 0, 0); remove.Click += async (_, _) => await RemoveGroupMemberAsync(chatId, member, state); controls.Children.Add(remove); }
            Grid.SetColumn(controls, 2); grid.Children.Add(controls);
        }
        card.Child = grid; return card;
    }

    private async Task ChangeMemberRoleAsync(int chatId, GroupMemberViewModel member, bool makeAdmin, GroupInfoDialogState state)
    {
        try { await _apiService.PutAsync<object, object>($"api/GroupManagement/{chatId}/members/{Uri.EscapeDataString(member.UserId)}/role", new { IsAdmin = makeAdmin }); member.Role = makeAdmin ? "Admin" : "Member"; await RefreshGroupMembersAsync(state); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Group Role", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task RemoveGroupMemberAsync(int chatId, GroupMemberViewModel member, GroupInfoDialogState state)
    {
        if (MessageBox.Show($"Remove {member.DisplayName} from this group?", "Remove Member", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { await _apiService.DeleteAsync($"api/Chat/{chatId}/members/{Uri.EscapeDataString(member.UserId)}"); await RefreshGroupMembersAsync(state); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Remove Member", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task AddGroupMemberAsync(GroupInfoDialogState state)
    {
        var username = PromptText("Add Member", "Username", string.Empty, state.Dialog);
        if (string.IsNullOrWhiteSpace(username)) return;

        try
        {
            var normalizedUsername = username.Trim().ToLowerInvariant();
            var candidates = await _apiService.GetAsync<List<GroupAddCandidate>>($"api/User/search?q={Uri.EscapeDataString(normalizedUsername)}") ?? [];
            var candidate = candidates.FirstOrDefault(x => string.Equals(x.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
            if (candidate == null)
            {
                System.Windows.MessageBox.Show($"User @{normalizedUsername} was not found.", "Add Member", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!candidate.AllowGroupAdds)
            {
                System.Windows.MessageBox.Show($"@{candidate.Username} does not allow other people to add them to groups.\n\nAsk them to enable group additions in their privacy settings first.", "Add Member", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            await _apiService.PostAsync<object, object>($"api/Chat/{state.ChatId}/members", new { Username = candidate.Username });
            await RefreshGroupMembersAsync(state);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Add Member", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ChangeGroupPictureAsync(GroupInfoDialogState state)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp", Title = "Choose group picture" }; if (picker.ShowDialog(state.Dialog) != true) return;
        try
        {
            var result = await _apiService.UploadFileAsync<GroupAvatarResult>($"api/Chat/{state.ChatId}/avatar", picker.FileName);
            if (result?.Chat != null) state.Chat.AvatarUrl = result.Chat.AvatarUrl;
            await LoadChatsAsync(); var refreshed = _chats.FirstOrDefault(x => x.Chat.Id == state.ChatId)?.Chat; if (refreshed != null) state.Chat = refreshed;
            await RefreshGroupAvatarAsync(state);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Group Picture", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task RefreshGroupAvatarAsync(GroupInfoDialogState state)
    {
        var avatar = state.ChatAvatar;
        avatar.Children.Clear();
        avatar.Children.Add(new Border { CornerRadius = new CornerRadius(46), Background = GetBrush("PrimarySoftBrush") });
        avatar.Children.Add(new TextBlock { Text = BuildGroupInitials(state.Chat.Name), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(state.Chat.AvatarUrl))
        {
            try
            {
                var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(state.Chat.AvatarUrl));
                if (image != null)
                {
                    var imageControl = new Image { Width = 92, Height = 92, Stretch = Stretch.UniformToFill, Source = image, IsHitTestVisible = false };
                    imageControl.Clip = new EllipseGeometry(new Point(46, 46), 46, 46);
                    avatar.Children.Add(imageControl);
                }
            }
            catch { }
        }
    }

    private async Task<Grid> BuildGroupAvatarAsync(ChatModel chat)
    {
        var avatar = new Grid { Width = 92, Height = 92, Margin = new Thickness(0, 0, 18, 0) };
        avatar.Children.Add(new Border { CornerRadius = new CornerRadius(46), Background = GetBrush("PrimarySoftBrush") });
        avatar.Children.Add(new TextBlock { Text = BuildGroupInitials(chat.Name), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(chat.AvatarUrl))
        {
            try
            {
                var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(chat.AvatarUrl));
                if (image != null)
                {
                    var imageControl = new Image { Width = 92, Height = 92, Stretch = Stretch.UniformToFill, Source = image, IsHitTestVisible = false };
                    imageControl.Clip = new EllipseGeometry(new Point(46, 46), 46, 46);
                    avatar.Children.Add(imageControl);
                }
            }
            catch { }
        }
        return avatar;
    }

    private static Button CreateActionButton(string content, string styleKey) => new() { Content = content, Height = 36, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 0, 12, 0), Style = (Style)Application.Current.FindResource(styleKey) };
    private static Border CreateRoleBadge(string? role) { var isOwner = string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase); var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase); var text = isOwner ? "OWNER" : isAdmin ? "ADMIN" : "MEMBER"; var brush = isOwner ? (Brush)Application.Current.FindResource("PrimaryBrush") : isAdmin ? (Brush)Application.Current.FindResource("SuccessBrush") : (Brush)Application.Current.FindResource("SecondaryTextBrush"); return new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(7, 2, 7, 2), CornerRadius = new CornerRadius(6), Background = (Brush)Application.Current.FindResource("PanelBackgroundBrush"), Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = brush } }; }
    private Brush GetBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    private static string? PromptText(string title, string label, string initialValue, Window owner) { var dialog = new Window { Title = title, Width = 390, Height = 190, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = (Brush)Application.Current.FindResource("PanelBackgroundBrush") }; var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.FindResource("TextBrush"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) }); var box = new TextBox { Height = 40, Text = initialValue, Padding = new Thickness(10), Background = (Brush)Application.Current.FindResource("InputBackgroundBrush"), Foreground = (Brush)Application.Current.FindResource("TextBrush"), BorderBrush = (Brush)Application.Current.FindResource("BorderBrush") }; panel.Children.Add(box); var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; string? value = null; var cancel = new Button { Content = "Cancel", Width = 82, Height = 34, Margin = new Thickness(0, 0, 7, 0), Style = (Style)Application.Current.FindResource("SecondaryButtonStyle") }; cancel.Click += (_, _) => dialog.Close(); var ok = new Button { Content = "OK", Width = 82, Height = 34, Style = (Style)Application.Current.FindResource("PrimaryButtonStyle") }; ok.Click += (_, _) => { value = box.Text.Trim(); dialog.Close(); }; buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons); dialog.Content = panel; dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); }; dialog.ShowDialog(); return value; }

    private sealed class GroupAddCandidate { public string Id { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public bool AllowGroupAdds { get; set; } }
    private sealed class GroupInfoDialogState
    {
        public Window Dialog { get; }
        public ChatModel Chat { get; set; }
        public Grid ChatAvatar { get; }
        public TextBlock TitleText { get; }
        public TextBlock MemberCountText { get; }
        public StackPanel MemberStack { get; }
        public bool IsOwner { get; }
        public bool CanManage { get; }
        public int ChatId { get; }
        public GroupInfoDialogState(Window dialog, ChatModel chat, Grid chatAvatar, TextBlock titleText, TextBlock memberCountText, StackPanel memberStack, bool isOwner, bool canManage, int chatId) { Dialog = dialog; Chat = chat; ChatAvatar = chatAvatar; TitleText = titleText; MemberCountText = memberCountText; MemberStack = memberStack; IsOwner = isOwner; CanManage = canManage; ChatId = chatId; }
    }
    private sealed class GroupMemberViewModel { public string UserId { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string? AvatarUrl { get; set; } public string Role { get; set; } = "Member"; public DateTime JoinedAt { get; set; } }
    private sealed class GroupAvatarResult { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; } }
}