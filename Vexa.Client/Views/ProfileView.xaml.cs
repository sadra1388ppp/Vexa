using Microsoft.Win32;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class ProfileView : UserControl
{
    public event Action? BackToChatRequested; public event Action? ContactsRequested; public event Action? SessionExpired;
    private readonly ApiService _apiService = new(); private ProfileModel? _profile; private bool _busy;
    public ProfileView() { InitializeComponent(); Loaded += ProfileView_Loaded; }
    private async void ProfileView_Loaded(object sender, RoutedEventArgs e) { await LoadProfileAsync(); InitializePrivacyUi(); }
    private async Task LoadProfileAsync()
    {
        if (!AuthState.IsAuthenticated) return;
        try
        {
            _profile = await _apiService.GetAsync<ProfileModel>("api/User/profile/me");
            if (_profile == null) { ShowFeedback("Could not load your profile."); return; }
            DataContext = _profile;
            DisplayNameBox.Text = _profile.DisplayName; UserIdBox.Text = _profile.Username; EmailBox.Text = _profile.Email;
            PhoneBox.Text = _profile.PhoneNumber ?? string.Empty; PhoneHintText.Visibility = string.IsNullOrEmpty(PhoneBox.Text) ? Visibility.Visible : Visibility.Collapsed; BioBox.Text = _profile.Bio;
            await LoadAvatarImageAsync(); UpdateProfileTextUi();
        }
        catch (Exception ex) { ShowFeedback($"Could not load profile: {ex.Message}"); }
    }
    private async Task LoadAvatarImageAsync()
    {
        AvatarImage.Source = null;
        AvatarInitialsText.Visibility = Visibility.Visible;
        if (_profile == null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return;
        try
        {
            var endpoint = $"api/User/profile/{Uri.EscapeDataString(_profile.Id)}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var bytes = await _apiService.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0) return;
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; image.StreamSource = stream; image.EndInit(); image.Freeze();
            AvatarImage.Source = image;
            AvatarInitialsText.Visibility = Visibility.Collapsed;
        }
        catch { AvatarImage.Source = null; AvatarInitialsText.Visibility = Visibility.Visible; }
    }
    private void UpdateProfileTextUi()
    {
        if (_profile == null) return; AvatarInitialsText.Text = GetInitials(_profile.DisplayName, _profile.Username); ProfileDisplayNameText.Text = _profile.DisplayName; ProfileUserIdText.Text = $"@{_profile.Username}"; ProfileBioText.Text = string.IsNullOrWhiteSpace(_profile.Bio) ? "Add a short bio to tell people about yourself." : _profile.Bio; var online = AuthState.IsAuthenticated; StatusText.Text = online ? "● Online" : "● Offline"; StatusText.Foreground = online ? Brushes.LimeGreen : (Brush)FindResource("SecondaryTextBrush"); ProfileStatusDot.Fill = online ? Brushes.LimeGreen : (Brush)FindResource("SecondaryTextBrush"); PresenceSummaryText.Text = online ? "Visible as online" : "Visible as offline"; LastSeenText.Text = online ? "Active now" : (_profile.LastSeenAt.HasValue ? $"Last seen {FormatLastSeen(_profile.LastSeenAt.Value)}" : "Last seen not available"); JoinedText.Text = $"Joined {_profile.CreatedAt.ToLocalTime():dd MMM yyyy}"; CopyUserIdButton.ToolTip = $"Copy @{_profile.Username}";
    }
    private void PhoneBox_TextChanged(object sender, TextChangedEventArgs e) => PhoneHintText.Visibility = string.IsNullOrEmpty(PhoneBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    private void PhoneBox_PreviewTextInput(object sender, TextCompositionEventArgs e) { if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => ch < '0' || ch > '9')) { e.Handled = true; return; } var proposed = GetProposedText(PhoneBox, e.Text); if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0')) e.Handled = true; }
    private void PhoneBox_Pasting(object sender, DataObjectPastingEventArgs e) { if (!e.DataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; } var pasted = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty; var proposed = GetProposedText(PhoneBox, pasted); if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0') || proposed.Any(ch => ch < '0' || ch > '9')) e.CancelCommand(); }
    private static string GetProposedText(TextBox box, string inserted) => box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, inserted);
    private static bool IsValidPhone(string phone) => phone.Length == 11 && phone[0] == '0' && phone.All(ch => ch >= '0' && ch <= '9');
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile == null) return; if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || string.IsNullOrWhiteSpace(UserIdBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text) || string.IsNullOrWhiteSpace(PhoneBox.Text)) { ShowFeedback("Display name, Username, Email and Phone Number are required."); return; } if (!IsValidPhone(PhoneBox.Text.Trim())) { ShowFeedback("Phone number must contain exactly 11 digits and start with 0."); return; } if (BioBox.Text.Length > 160) { ShowFeedback("Bio must be 160 characters or less."); return; }
        _busy = true; SaveButton.IsEnabled = false;
        try { var internalId = _profile.Id; var request = new UpdateProfileRequest { DisplayName = DisplayNameBox.Text.Trim(), Email = EmailBox.Text.Trim(), PhoneNumber = PhoneBox.Text.Trim(), Bio = BioBox.Text.Trim(), NewUsername = UserIdBox.Text.Trim() }; var result = await _apiService.PutAsync<UpdateProfileRequest, ProfileActionResponse>($"api/User/{Uri.EscapeDataString(internalId)}", request); if (result?.User == null) { ShowFeedback("Profile could not be updated. Check your values and try again."); return; } _profile = result.User; DataContext = _profile; DisplayNameBox.Text = _profile.DisplayName; UserIdBox.Text = _profile.Username; EmailBox.Text = _profile.Email; PhoneBox.Text = _profile.PhoneNumber ?? string.Empty; BioBox.Text = _profile.Bio; await LoadAvatarImageAsync(); UpdateProfileTextUi(); ShowFeedback(result.Message); if (!string.Equals(AuthState.UserId, _profile.Id, StringComparison.Ordinal)) { AuthState.Clear(); SessionExpired?.Invoke(); } else AuthState.UpdateProfile(_profile.Username, _profile.DisplayName, _profile.Email); }
        catch (Exception ex) { ShowFeedback($"Profile update failed: {ex.Message}"); }
        finally { _busy = false; SaveButton.IsEnabled = true; }
    }
    private async void ChangePictureButton_Click(object sender, RoutedEventArgs e) { if (_busy || !AuthState.IsAuthenticated) return; var dialog = new OpenFileDialog { Title = "Choose profile picture", Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp|All files|*.*", CheckFileExists = true, Multiselect = false }; if (dialog.ShowDialog() != true) return; _busy = true; SaveButton.IsEnabled = false; try { var result = await _apiService.UploadFileAsync<ProfileActionResponse>($"api/User/{Uri.EscapeDataString(AuthState.UserId)}/avatar", dialog.FileName); if (result?.User == null) ShowFeedback("Profile picture upload failed."); else { _profile = result.User; DataContext = _profile; await LoadAvatarImageAsync(); UpdateProfileTextUi(); ShowFeedback(result.Message); } } catch (Exception ex) { ShowFeedback($"Picture upload failed: {ex.Message}"); } finally { _busy = false; SaveButton.IsEnabled = true; } }
    private async void RemovePictureButton_Click(object sender, RoutedEventArgs e) { if (_busy || _profile == null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return; if (MessageBox.Show("Remove your profile picture?", "NovaChat", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; _busy = true; try { var removed = await _apiService.DeleteAsync($"api/User/{Uri.EscapeDataString(_profile.Id)}/avatar"); if (removed) { _profile.AvatarUrl = null; AvatarImage.Source = null; AvatarInitialsText.Visibility = Visibility.Visible; DataContext = _profile; UpdateProfileTextUi(); ShowFeedback("Profile picture removed."); } else ShowFeedback("Profile picture could not be removed."); } catch (Exception ex) { ShowFeedback($"Could not remove picture: {ex.Message}"); } finally { _busy = false; } }
    private void CopyUserIdButton_Click(object sender, RoutedEventArgs e) { if (_profile == null || string.IsNullOrWhiteSpace(_profile.Username)) return; Clipboard.SetText(_profile.Username); ShowFeedback($"Copied @{_profile.Username} to clipboard."); }
    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e) { if (_busy || !AuthState.IsAuthenticated) return; var dialog = new Window { Title = "Change password", Width = 420, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize, Background = (Brush)FindResource("PanelBackgroundBrush") }; var root = new StackPanel { Margin = new Thickness(24) }; root.Children.Add(new TextBlock { Text = "Change your password", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") }); root.Children.Add(new TextBlock { Text = "Choose a new password you do not reuse elsewhere.", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 4, 0, 18) }); var currentBox = CreatePasswordBox(root, "Current password"); var newBox = CreatePasswordBox(root, "New password"); var confirmBox = CreatePasswordBox(root, "Confirm new password"); var feedback = new TextBlock { Foreground = (Brush)FindResource("PrimaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 10) }; root.Children.Add(feedback); var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var cancel = new Button { Content = "Cancel", Width = 82, Height = 36, Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent, Foreground = (Brush)FindResource("PrimaryBrush"), BorderBrush = (Brush)FindResource("PrimaryBrush") }; var save = new Button { Content = "Update", Width = 82, Height = 36, Background = (Brush)FindResource("PrimaryBrush"), Foreground = Brushes.White, BorderThickness = new Thickness(0) }; actions.Children.Add(cancel); actions.Children.Add(save); root.Children.Add(actions); cancel.Click += (_, _) => dialog.Close(); save.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(currentBox.Password) || string.IsNullOrWhiteSpace(newBox.Password) || string.IsNullOrWhiteSpace(confirmBox.Password)) { feedback.Text = "Fill in all password fields."; return; } if (!string.Equals(newBox.Password, confirmBox.Password, StringComparison.Ordinal)) { feedback.Text = "New passwords do not match."; return; } if (newBox.Password.Length < 6) { feedback.Text = "New password must be at least 6 characters."; return; } if (string.Equals(currentBox.Password, newBox.Password, StringComparison.Ordinal)) { feedback.Text = "New password must be different from the current password."; return; } save.IsEnabled = false; cancel.IsEnabled = false; try { var response = await _apiService.PutAsync<ChangePasswordRequest, SimpleMessageResponse>($"api/User/{Uri.EscapeDataString(AuthState.UserId)}/password", new ChangePasswordRequest { CurrentPassword = currentBox.Password, NewPassword = newBox.Password }); if (response == null) { feedback.Text = "Password could not be changed. Check your current password and try again."; return; } feedback.Text = response.Message; ShowFeedback(response.Message); if (response.Message.Contains("success", StringComparison.OrdinalIgnoreCase)) dialog.Close(); } catch (Exception ex) { feedback.Text = $"Password update failed: {ex.Message}"; } finally { save.IsEnabled = true; cancel.IsEnabled = true; } }; dialog.Content = root; dialog.Loaded += (_, _) => currentBox.Focus(); dialog.ShowDialog(); }
    private static PasswordBox CreatePasswordBox(Panel parent, string label) { parent.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("TextBrush"), Margin = new Thickness(0, 6, 0, 5) }); var box = new PasswordBox { Height = 40, Padding = new Thickness(10), Background = (Brush)Application.Current.FindResource("InputBackgroundBrush"), Foreground = (Brush)Application.Current.FindResource("TextBrush"), BorderBrush = (Brush)Application.Current.FindResource("BorderBrush") }; parent.Children.Add(box); return box; }
    private static string GetInitials(string displayName, string username) { var value = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(); if (string.IsNullOrWhiteSpace(value)) return "?"; var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries); return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant(); }
    private static string FormatLastSeen(DateTime value) { var local = value.ToLocalTime(); return local.Date == DateTime.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy HH:mm"); }
    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthState.IsAuthenticated)
        {
            SessionExpired?.Invoke();
            return;
        }

        try
        {
            await _apiService.PostAsync<SimpleMessageResponse>("api/User/logout", new { });
        }
        catch (HttpRequestException)
        {
            // The local session is still cleared even if the server is unreachable.
        }
        finally
        {
            AuthState.Clear();
            SessionExpired?.Invoke();
        }
    }

    private void ShowFeedback(string message) => FeedbackText.Text = message; private void BackToChatButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke(); private void ContactsButton_Click(object sender, RoutedEventArgs e) => ContactsRequested?.Invoke();
    private sealed class ChangePasswordRequest { public string CurrentPassword { get; set; } = string.Empty; public string NewPassword { get; set; } = string.Empty; } private sealed class SimpleMessageResponse { public string Message { get; set; } = string.Empty; }
}
