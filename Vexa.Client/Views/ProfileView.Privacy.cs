using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    private ComboBox? _messagePrivacyBox;
    private CheckBox? _allowGroupAddsBox;
    private Button? _savePrivacyButton;
    private Button? _chatRequestsButton;
    private bool _privacyUiInjected;
    private bool _privacyBusy;

    private void InitializePrivacyUi()
    {
        InjectPrivacyControls();
        _ = LoadPrivacySettingsAsync();
    }

    private void InjectPrivacyControls()
    {
        if (_privacyUiInjected ||
            PresenceSummaryText.Parent is not Grid presenceGrid ||
            presenceGrid.Parent is not Border presenceBorder ||
            presenceBorder.Parent is not StackPanel securityPanel)
        {
            return;
        }

        var privacyHeader = new TextBlock
        {
            Text = "Messaging & Group Privacy",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 18, 0, 9)
        };
        securityPanel.Children.Add(privacyHeader);

        var messageBorder = new Border
        {
            Padding = new Thickness(14),
            Background = (Brush)FindResource("InputBackgroundBrush"),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var messageGrid = new Grid();
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var messageStack = new StackPanel();
        messageStack.Children.Add(new TextBlock { Text = "Who can message me?", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        messageStack.Children.Add(new TextBlock { Text = "Choose whether new conversations start immediately or require your approval.", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 3, 10, 0), TextWrapping = TextWrapping.Wrap });
        messageGrid.Children.Add(messageStack);
        _messagePrivacyBox = new ComboBox { Width = 145, Height = 34, VerticalAlignment = VerticalAlignment.Center, ItemsSource = new[] { "Everybody", "Requests only" }, SelectedIndex = 0 };
        Grid.SetColumn(_messagePrivacyBox, 1);
        messageGrid.Children.Add(_messagePrivacyBox);
        messageBorder.Child = messageGrid;
        securityPanel.Children.Add(messageBorder);

        var groupBorder = new Border
        {
            Padding = new Thickness(14),
            Background = (Brush)FindResource("InputBackgroundBrush"),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _allowGroupAddsBox = new CheckBox
        {
            IsChecked = true,
            Foreground = (Brush)FindResource("TextBrush"),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Allow other people to add me to groups", FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = "Turn this off to prevent direct group additions.", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        groupBorder.Child = _allowGroupAddsBox;
        securityPanel.Children.Add(groupBorder);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _chatRequestsButton = new Button { Content = "Chat requests", Height = 36, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        _chatRequestsButton.Click += async (_, _) => await ShowChatRequestsAsync();
        actions.Children.Add(_chatRequestsButton);
        _savePrivacyButton = new Button { Content = "Save privacy", Height = 36, Padding = new Thickness(14, 0, 14, 0), Style = (Style)FindResource("PrimaryButtonStyle") };
        _savePrivacyButton.Click += async (_, _) => await SavePrivacySettingsAsync();
        actions.Children.Add(_savePrivacyButton);
        securityPanel.Children.Add(actions);
        _privacyUiInjected = true;
    }

    private async Task LoadPrivacySettingsAsync()
    {
        if (_messagePrivacyBox == null || _allowGroupAddsBox == null || !AuthState.IsAuthenticated) return;
        try
        {
            var settings = await _apiService.GetAsync<PrivacySettingsModel>("api/Privacy");
            if (settings == null) return;
            _messagePrivacyBox.SelectedItem = string.Equals(settings.MessagePrivacy, "Requests", StringComparison.OrdinalIgnoreCase) ? "Requests only" : "Everybody";
            _allowGroupAddsBox.IsChecked = settings.AllowGroupAdds;
        }
        catch (Exception ex)
        {
            ShowFeedback($"Could not load privacy settings: {ex.Message}");
        }
    }

    private async Task SavePrivacySettingsAsync()
    {
        if (_privacyBusy || _messagePrivacyBox == null || _allowGroupAddsBox == null) return;
        _privacyBusy = true;
        if (_savePrivacyButton != null) _savePrivacyButton.IsEnabled = false;
        try
        {
            var response = await _apiService.PutAsync<PrivacySettingsModel, PrivacyActionResponse>("api/Privacy", new PrivacySettingsModel
            {
                MessagePrivacy = string.Equals(_messagePrivacyBox.SelectedItem?.ToString(), "Requests only", StringComparison.OrdinalIgnoreCase) ? "Requests" : "Everybody",
                AllowGroupAdds = _allowGroupAddsBox.IsChecked != false
            });
            ShowFeedback(response?.Message ?? "Privacy settings updated.");
            if (_profile != null)
            {
                _profile.MessagePrivacy = response?.MessagePrivacy ?? _profile.MessagePrivacy;
                _profile.AllowGroupAdds = response?.AllowGroupAdds ?? _profile.AllowGroupAdds;
                DataContext = _profile;
            }
        }
        catch (Exception ex)
        {
            ShowFeedback($"Privacy update failed: {ex.Message}");
        }
        finally
        {
            _privacyBusy = false;
            if (_savePrivacyButton != null) _savePrivacyButton.IsEnabled = true;
        }
    }

    private async Task ShowChatRequestsAsync()
    {
        if (!AuthState.IsAuthenticated) return;
        try
        {
            var requests = await _apiService.GetAsync<List<ChatRequestModel>>("api/ChatRequests/incoming") ?? [];
            var dialog = new Window
            {
                Title = "Chat Requests",
                Width = 520,
                Height = 620,
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("PanelBackgroundBrush")
            };
            var root = new DockPanel { Margin = new Thickness(22) };
            var title = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            title.Children.Add(new TextBlock { Text = "Chat Requests", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
            title.Children.Add(new TextBlock { Text = "Approve people before a new private conversation becomes active.", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
            var list = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel();
            if (requests.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "No pending chat requests.", FontSize = 14, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) });
            }
            else
            {
                foreach (var request in requests)
                {
                    var card = new Border { Padding = new Thickness(14), Background = (Brush)FindResource("InputBackgroundBrush"), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 10) };
                    var stack = new StackPanel();
                    stack.Children.Add(new TextBlock { Text = request.RequesterDisplayName, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
                    stack.Children.Add(new TextBlock { Text = $"@{request.RequesterUsername}", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 10) });
                    var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                    var reject = new Button { Content = "Reject", Height = 34, Width = 88, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
                    var accept = new Button { Content = "Accept", Height = 34, Width = 88, Style = (Style)FindResource("PrimaryButtonStyle") };
                    reject.Click += async (_, _) => { try { await _apiService.PostAsync<object, PrivacyActionResponse>($"api/ChatRequests/{request.Id}/reject", new { }); dialog.Close(); await ShowChatRequestsAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Chat Request", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                    accept.Click += async (_, _) => { try { var result = await _apiService.PostAsync<object, ChatRequestAcceptResponse>($"api/ChatRequests/{request.Id}/accept", new { }); dialog.Close(); if (result?.Chat?.ChatId > 0) ShowFeedback("Chat request accepted. The conversation is now active."); else ShowFeedback("Chat request accepted."); await ShowChatRequestsAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Chat Request", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                    actions.Children.Add(reject); actions.Children.Add(accept); stack.Children.Add(actions); card.Child = stack; panel.Children.Add(card);
                }
            }
            list.Content = panel;
            root.Children.Add(list);
            dialog.Content = root;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowFeedback($"Could not load chat requests: {ex.Message}");
        }
    }

    private sealed class PrivacySettingsModel
    {
        public string MessagePrivacy { get; set; } = "Everybody";
        public bool AllowGroupAdds { get; set; } = true;
    }

    private sealed class PrivacyActionResponse
    {
        public string Message { get; set; } = string.Empty;
        public string MessagePrivacy { get; set; } = "Everybody";
        public bool AllowGroupAdds { get; set; } = true;
    }

    private sealed class ChatRequestModel
    {
        public long Id { get; set; }
        public string RequesterUserId { get; set; } = string.Empty;
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
    }

    private sealed class ChatRequestAcceptResponse
    {
        public string Message { get; set; } = string.Empty;
        public RequestChatModel? Chat { get; set; }
    }

    private sealed class RequestChatModel
    {
        public int ChatId { get; set; }
    }
}
