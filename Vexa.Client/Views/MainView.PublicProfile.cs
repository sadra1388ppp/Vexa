using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly AvatarImageService _avatarImageService = new(new ApiService());

    private async void ChatUserNameText_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId)) return;
        try
        {
            var userId = _currentOtherUserId.Trim();
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null)
            {
                MessageBox.Show("This user's profile could not be loaded.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try { profile.IsOnline = await _hubConnection.InvokeAsync<bool>("IsUserOnline", userId); }
                catch { profile.IsOnline = IsUserOnline(userId); }
            }
            else
            {
                profile.IsOnline = IsUserOnline(userId);
            }

            ShowPublicProfile(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load this profile.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshCurrentChatAvatarAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId))
        {
            ChatHeaderAvatarImage.Source = null;
            ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            ChatAvatarInitialsText.Visibility = Visibility.Visible;
            ChatAvatarInitialsText.Text = "N";
            return;
        }

        try
        {
            var userId = _currentOtherUserId.Trim();
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null) return;

            var online = IsUserOnline(userId);
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try { online = await _hubConnection.InvokeAsync<bool>("IsUserOnline", userId); }
                catch { }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ChatAvatarInitialsText.Text = GetPublicProfileInitials(profile.DisplayName, profile.Id);
                ChatStatusText.Text = online ? "Online" : "Offline";
                ChatStatusIndicator.Fill = online ? Brushes.LimeGreen : Brushes.Gray;
                ChatAvatarStatusDot.Fill = online ? Brushes.LimeGreen : Brushes.Gray;
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            });

            if (string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

            var bitmap = await _avatarImageService.LoadAsync(profile.Id, profile.AvatarUrl);
            if (bitmap == null || !string.Equals(_currentOtherUserId, profile.Id, StringComparison.OrdinalIgnoreCase)) return;

            await Dispatcher.InvokeAsync(() =>
            {
                ChatHeaderAvatarImage.Source = bitmap;
                ChatHeaderAvatarImage.Visibility = Visibility.Visible;
                ChatAvatarInitialsText.Visibility = Visibility.Collapsed;
            });
        }
        catch { }
    }

    private void ShowPublicProfile(ProfileModel profile)
    {
        var window = new Window
        {
            Title = $"{profile.DisplayName} · Profile",
            Width = 390,
            Height = 470,
            MinWidth = 350,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new StackPanel { Margin = new Thickness(28) };
        var avatarGrid = new Grid { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 18) };
        avatarGrid.Children.Add(new Ellipse { Fill = (Brush)FindResource("SelectedChatBrush") });
        var initials = new TextBlock { Text = GetPublicProfileInitials(profile.DisplayName, profile.Id), Foreground = (Brush)FindResource("PrimaryBrush"), FontSize = 38, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        avatarGrid.Children.Add(initials);
        root.Children.Add(avatarGrid);
        root.Children.Add(new TextBlock { Text = profile.DisplayName, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"@{profile.Id}", FontSize = 13, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 14) });
        root.Children.Add(new TextBlock { Text = profile.IsOnline ? "● Online" : "● Offline", FontSize = 12, Foreground = profile.IsOnline ? Brushes.LimeGreen : (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) });
        if (!string.IsNullOrWhiteSpace(profile.Bio)) root.Children.Add(new TextBlock { Text = profile.Bio, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(10, 0, 10, 18) });
        root.Children.Add(new TextBlock { Text = profile.IsOnline ? "Active now" : (profile.LastSeenAt.HasValue ? $"Last seen {FormatPublicLastSeen(profile.LastSeenAt.Value)}" : "Last seen not available"), FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"Joined {profile.CreatedAt.ToLocalTime():dd MMM yyyy}", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 20) });
        var closeButton = new Button { Content = "Close", Width = 100, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, Background = (Brush)FindResource("PrimaryBrush"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        closeButton.Click += (_, _) => window.Close();
        root.Children.Add(closeButton);
        window.Content = root;

        _ = LoadPublicProfileAvatarAsync(profile, avatarGrid, initials);
        window.ShowDialog();
    }

    private async Task LoadPublicProfileAvatarAsync(ProfileModel profile, Grid avatarGrid, TextBlock initials)
    {
        if (string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

        try
        {
            var bitmap = await _avatarImageService.LoadAsync(profile.Id, profile.AvatarUrl);
            if (bitmap == null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var image = avatarGrid.Children.OfType<Image>().FirstOrDefault();
                if (image == null)
                {
                    image = new Image
                    {
                        Width = 112,
                        Height = 112,
                        Stretch = Stretch.UniformToFill,
                        Clip = new EllipseGeometry(new Point(56, 56), 56, 56)
                    };
                    avatarGrid.Children.Add(image);
                }

                image.Source = bitmap;
                Panel.SetZIndex(image, 2);
                initials.Visibility = Visibility.Collapsed;
            });
        }
        catch { }
    }

    private static string GetPublicProfileInitials(string displayName, string id)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant();
    }

    private static string FormatPublicLastSeen(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTime.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy HH:mm");
    }
}
