using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly bool _groupDeletePreviewHandlerRegistered = RegisterGroupDeletePreviewHandler();

    private static bool RegisterGroupDeletePreviewHandler()
    {
        EventManager.RegisterClassHandler(typeof(Button), UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnGroupDeletePreviewMouseDown));
        return true;
    }

    private static async void OnGroupDeletePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ChatListItem item || !item.Chat.IsGroup) return;
        if (!string.Equals(button.ToolTip?.ToString(), "Delete this conversation", StringComparison.OrdinalIgnoreCase)) return;
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source) != button) return;
        e.Handled = true;
        if (button.DataContext is not ChatListItem currentItem) return;
        if (button.Tag is not null || !button.IsEnabled) return;
        var view = FindVisualParent<MainView>(button);
        if (view == null) return;
        await view.HandleGroupDeleteFromListAsync(currentItem);
    }

    private async Task HandleGroupDeleteFromListAsync(ChatListItem item)
    {
        try
        {
            var members = await _apiService.GetAsync<List<GroupDeleteMemberModel>>($"api/Chat/{item.Chat.Id}/members") ?? [];
            var currentMember = members.FirstOrDefault(m => string.Equals(m.UserId, Services.AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            if (currentMember == null)
            {
                MessageBox.Show("You are no longer a member of this group.", "Group", MessageBoxButton.OK, MessageBoxImage.Information);
                await RemoveChatFromLocalUiAsync(item.Chat.Id);
                return;
            }

            var isOwner = string.Equals(currentMember.Role, "Owner", StringComparison.OrdinalIgnoreCase);
            if (!isOwner)
            {
                var leave = MessageBox.Show($"Leave '{item.Chat.Name}'?\n\nYou will leave this group and it will be removed from your conversations.", "Leave Group", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (leave != MessageBoxResult.Yes) return;
                await _apiService.PostAsync<object, object>($"api/GroupManagement/{item.Chat.Id}/leave", new { });
                await RemoveChatFromLocalUiAsync(item.Chat.Id);
                return;
            }

            if (!await ShowProfessionalGroupDeleteDialogAsync(item, members.Count)) return;
            var deleted = await _apiService.DeleteAsync($"api/GroupManagement/{item.Chat.Id}");
            if (!deleted)
            {
                MessageBox.Show("The group could not be deleted. Please make sure the server is running and try again.", "Delete Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await RemoveChatFromLocalUiAsync(item.Chat.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the group.\n\n{ex.Message}", "Delete Group", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> ShowProfessionalGroupDeleteDialogAsync(ChatListItem item, int memberCount)
    {
        var groupName = string.IsNullOrWhiteSpace(item.Chat.Name) ? "Unnamed group" : item.Chat.Name.Trim();
        var dialog = new Window { Title = "Delete Group", Width = 520, Height = 405, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = GetBrush("AppBackgroundBrush") };
        var root = new Grid { Margin = new Thickness(26) };
        for (var i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(17), Background = GetBrush("PrimarySoftBrush") };
        icon.Child = new TextBlock { Text = "♟", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(icon);
        var title = new StackPanel { Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Delete group?", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = GetBrush("TextBrush") });
        title.Children.Add(new TextBlock { Text = groupName, FontSize = 12, Foreground = GetBrush("SecondaryTextBrush"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        header.Children.Add(title); Grid.SetRow(header, 0); root.Children.Add(header);

        var line = new Border { Height = 1, Background = GetBrush("BorderBrush"), Margin = new Thickness(0, 20, 0, 17) }; Grid.SetRow(line, 1); root.Children.Add(line);
        var description = new TextBlock { Text = "This will permanently delete the group from NovaChat. All membership records and message history belonging to this group will be removed from the server.", FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = GetBrush("TextBrush") }; Grid.SetRow(description, 2); root.Children.Add(description);
        var warning = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Background = GetBrush("InputBackgroundBrush"), BorderBrush = GetBrush("BorderBrush"), BorderThickness = new Thickness(1) };
        var warningStack = new StackPanel(); warningStack.Children.Add(new TextBlock { Text = "PERMANENT ACTION", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = GetBrush("PrimaryBrush") }); warningStack.Children.Add(new TextBlock { Text = $"{memberCount} member{(memberCount == 1 ? "" : "s")} will lose access to this group and its messages.", FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = GetBrush("TextBrush") }); warning.Child = warningStack; Grid.SetRow(warning, 3); root.Children.Add(warning);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 105, Height = 42, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        var confirm = new Button { Content = "Delete group", Width = 135, Height = 42, Style = (Style)FindResource("DangerButtonStyle") };
        cancel.Click += (_, _) => dialog.DialogResult = false; confirm.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(cancel); actions.Children.Add(confirm); Grid.SetRow(actions, 4); root.Children.Add(actions);
        dialog.Content = root; dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }

    private sealed class GroupDeleteMemberModel { public string UserId { get; set; } = string.Empty; public string Role { get; set; } = string.Empty; }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null) { if (child is T match) return match; child = VisualTreeHelper.GetParent(child); }
        return null;
    }
}
