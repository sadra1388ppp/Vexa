using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _chatRequestUiRegistered;
    private DispatcherTimer? _chatRequestTimer;
    private StackPanel? _conversationRequestsPanel;
    private TextBlock? _conversationRequestsHeader;
    private bool _chatRequestBusy;

    private static void RegisterChatRequestHandlers()
    {
        if (_chatRequestUiRegistered) return;
        _chatRequestUiRegistered = true;
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnMainViewChatButtonClicked));
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnMainViewChatRequestsLoaded));
    }

    private static async void OnMainViewChatButtonClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button) return;
        var content = button.Content?.ToString();
        if (content is not ("＋   New conversation" or "＋   Start a new chat")) return;
        if (FindAncestor<MainView>(button) is not MainView view) return;
        e.Handled = true;
        await view.OpenChatRequestFlowAsync();
    }

    private static async void OnMainViewChatRequestsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view || view._chatRequestTimer != null) return;
        view._chatRequestTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        view._chatRequestTimer.Tick += async (_, _) => await view.RefreshConversationChatRequestsAsync();
        view._chatRequestTimer.Start();
        await view.RefreshConversationChatRequestsAsync();
    }

    private async Task OpenChatRequestFlowAsync()
    {
        if (_chatRequestBusy || !AuthState.IsAuthenticated) return;
        var dialog = new Window
        {
            Title = "New Conversation",
            Width = 430,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "Start a conversation", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
        root.Children.Add(new TextBlock { Text = "Enter a username. Their privacy settings determine whether the chat starts immediately or needs approval.", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 16) });
        var box = new TextBox { Height = 42, Padding = new Thickness(11), ToolTip = "Username" };
        root.Children.Add(box);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 85, Height = 38, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        var start = new Button { Content = "Continue", Width = 95, Height = 38, Style = (Style)FindResource("PrimaryButtonStyle") };
        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(start); root.Children.Add(actions);
        box.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Enter) start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        dialog.Content = root;
        dialog.Loaded += (_, _) => box.Focus();
        if (dialog.ShowDialog() != true) return;

        var username = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.Equals(username, AuthState.Username, StringComparison.OrdinalIgnoreCase)) return;

        _chatRequestBusy = true;
        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>("api/Chat", new CreateChatRequest { Username = username });
            if (result == null)
            {
                MessageBox.Show("The server returned no response.", "New Conversation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (result.RequestPending)
            {
                await RefreshConversationChatRequestsAsync();
                return;
            }
            if (result.Chat == null)
            {
                MessageBox.Show(string.IsNullOrWhiteSpace(result.Message) ? "The conversation could not be created." : result.Message, "New Conversation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await LoadChatsAsync();
            var item = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            if (item != null) await OpenChatAsync(item.Chat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the conversation.\n\n{ex.Message}", "New Conversation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _chatRequestBusy = false; }
    }

    private void EnsureConversationChatRequestsUi()
    {
        if (_conversationRequestsPanel != null && _conversationRequestsHeader != null) return;
        if (ChatsList.Parent is not StackPanel parent) return;
        var chatListIndex = parent.Children.IndexOf(ChatsList);
        if (chatListIndex < 0) return;

        _conversationRequestsHeader = new TextBlock
        {
            Text = "CHAT REQUESTS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        _conversationRequestsPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        parent.Children.Insert(chatListIndex, _conversationRequestsHeader);
        parent.Children.Insert(chatListIndex + 1, _conversationRequestsPanel);
    }

    private async Task RefreshConversationChatRequestsAsync()
    {
        if (!AuthState.IsAuthenticated) return;
        try
        {
            await Dispatcher.InvokeAsync(EnsureConversationChatRequestsUi);
            var incoming = await _apiService.GetAsync<List<ConversationChatRequestModel>>("api/ChatRequests/incoming") ?? [];
            var outgoing = await _apiService.GetAsync<List<ConversationChatRequestModel>>("api/ChatRequests/outgoing") ?? [];
            var items = new List<ConversationChatRequestItem>(incoming.Count + outgoing.Count);

            foreach (var request in incoming)
            {
                items.Add(new ConversationChatRequestItem
                {
                    RequestId = request.Id,
                    DisplayName = string.IsNullOrWhiteSpace(request.RequesterDisplayName) ? request.RequesterUsername : request.RequesterDisplayName,
                    Username = request.RequesterUsername,
                    Summary = "Someone sent you a chat request.",
                    IsIncoming = true
                });
            }

            foreach (var request in outgoing)
            {
                items.Add(new ConversationChatRequestItem
                {
                    RequestId = request.Id,
                    DisplayName = string.IsNullOrWhiteSpace(request.TargetDisplayName) ? request.TargetUsername : request.TargetDisplayName,
                    Username = request.TargetUsername,
                    Summary = "Chat request sent · Waiting for approval.",
                    IsIncoming = false
                });
            }

            items.Sort((a, b) => b.RequestId.CompareTo(a.RequestId));
            await Dispatcher.InvokeAsync(() => RenderConversationChatRequests(items));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Conversation chat request refresh failed: {ex.Message}");
        }
    }

    private void RenderConversationChatRequests(List<ConversationChatRequestItem> items)
    {
        EnsureConversationChatRequestsUi();
        if (_conversationRequestsPanel == null || _conversationRequestsHeader == null) return;

        _conversationRequestsPanel.Children.Clear();
        var visible = items.Count > 0;
        _conversationRequestsHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _conversationRequestsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        foreach (var request in items)
        {
            var card = new Border
            {
                Padding = new Thickness(10),
                Background = (Brush)FindResource("InputBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 0, 7)
            };
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("SelectedChatBrush"), VerticalAlignment = VerticalAlignment.Top };
            avatar.Child = new TextBlock { Text = request.Initials, Foreground = (Brush)FindResource("PrimaryBrush"), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(avatar);

            var content = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
            content.Children.Add(new TextBlock { Text = request.DisplayName, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = $"@{request.Username}", FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = request.Summary, FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });

            if (request.IsIncoming)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                var reject = new Button { Content = "Reject", Height = 28, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 0), Style = (Style)FindResource("SecondaryButtonStyle"), DataContext = request };
                var accept = new Button { Content = "Accept", Height = 28, Padding = new Thickness(9, 0, 9, 0), Style = (Style)FindResource("PrimaryButtonStyle"), DataContext = request };
                reject.Click += RejectConversationRequestButton_Click;
                accept.Click += AcceptConversationRequestButton_Click;
                actions.Children.Add(reject);
                actions.Children.Add(accept);
                content.Children.Add(actions);
            }

            Grid.SetColumn(content, 1);
            root.Children.Add(content);
            card.Child = root;
            _conversationRequestsPanel.Children.Add(card);
        }
    }

    private async void AcceptConversationRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ConversationChatRequestItem request } || request.RequestId <= 0) return;
        try
        {
            var result = await _apiService.PostAsync<object, RequestActionResponse>($"api/ChatRequests/{request.RequestId}/accept", new { });
            if (result == null) return;
            await LoadChatsAsync();
            await RefreshConversationChatRequestsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not accept the chat request.\n\n{ex.Message}", "Chat Request", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RejectConversationRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ConversationChatRequestItem request } || request.RequestId <= 0) return;
        try
        {
            var result = await _apiService.PostAsync<object, RequestActionResponse>($"api/ChatRequests/{request.RequestId}/reject", new { });
            if (result == null) return;
            await RefreshConversationChatRequestsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not reject the chat request.\n\n{ex.Message}", "Chat Request", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class ConversationChatRequestModel
    {
        public long Id { get; set; }
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
    }

    private sealed class ConversationChatRequestItem
    {
        public long RequestId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool IsIncoming { get; set; }
        public string Initials
        {
            get
            {
                var value = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName.Trim();
                if (string.IsNullOrWhiteSpace(value)) return "?";
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant();
            }
        }
    }

    private sealed class RequestActionResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
