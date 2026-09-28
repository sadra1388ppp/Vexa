using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaChat.Client.Services;
using NovaChat.Client.Views;

namespace NovaChat.Client
{
    public partial class MainWindow : Window
    {
        private bool _isOwner;
        private string? _pendingChatUsername;
        private MainView? _mainView;

        public MainWindow()
        {
            InitializeComponent();
            LoadLightTheme();
            ShowLogin();
        }

        public void ShowLogin()
        {
            _isOwner = false;
            _pendingChatUsername = null;
            NotificationService.Dispose();

            if (_mainView != null)
            {
                MainContainer.Children.Remove(_mainView);
                _mainView = null;
            }

            MainContainer.Children.Clear();

            LoginView loginView = new LoginView();
            loginView.CreateAccountRequested += ShowRegister;
            loginView.LoginSuccessful += HandleNormalUserLogin;
            loginView.OwnerLoginSuccessful += HandleOwnerLogin;
            MainContainer.Children.Add(loginView);
        }

        private void HandleNormalUserLogin() { _isOwner = false; ShowMain(); }
        private void HandleOwnerLogin() { _isOwner = true; ShowMain(); }

        public void ShowRegister()
        {
            MainContainer.Children.Clear();
            RegisterView registerView = new RegisterView();
            registerView.BackToLoginRequested += ShowLogin;
            MainContainer.Children.Add(registerView);
        }

        public void ShowMain()
        {
            MainContainer.Children.Clear();
            if (_mainView == null)
            {
                _mainView = new MainView();
                _mainView.ProfileRequested += ShowProfile;
                _mainView.SettingsRequested += ShowSettings;
            }
            _mainView.SetOwnerMode(_isOwner);
            MainContainer.Children.Add(_mainView);

            if (!string.IsNullOrWhiteSpace(_pendingChatUsername))
            {
                var username = _pendingChatUsername;
                _pendingChatUsername = null;
                _mainView.Loaded += OpenPendingChatOnce;

                async void OpenPendingChatOnce(object? sender, RoutedEventArgs e)
                {
                    _mainView!.Loaded -= OpenPendingChatOnce;
                    await _mainView.OpenChatWithUsernameAsync(username);
                }
            }
        }

        public void ShowManageUsers()
        {
            if (!_isOwner) return;
            MainContainer.Children.Clear();
            ManageUsersView manageUsersView = new ManageUsersView();
            manageUsersView.BackToChatRequested += ShowMain;
            MainContainer.Children.Add(manageUsersView);
        }

        public void ShowProfile()
        {
            MainContainer.Children.Clear();
            ProfileView profileView = new ProfileView();
            profileView.BackToChatRequested += ShowMain;
            profileView.ContactsRequested += ShowContacts;
            profileView.SessionExpired += ShowLogin;
            MainContainer.Children.Add(profileView);
        }

        public void ShowContacts()
        {
            MainContainer.Children.Clear();
            ContactsView contactsView = new ContactsView();
            contactsView.BackToChatRequested += ShowMain;
            contactsView.ChatRequested += username => { _pendingChatUsername = username; ShowMain(); };
            MainContainer.Children.Add(contactsView);
        }

        private async void LogoutAsync()
        {
            try
            {
                if (AuthState.IsAuthenticated)
                {
                    var api = new ApiService();
                    await api.PostAsync<object, LogoutResponse>("api/User/logout", new { });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logout request failed: {ex}");
            }
            finally
            {
                AuthState.Clear();
                ShowLogin();
            }
        }

        private sealed class LogoutResponse
        {
            public string? Message { get; set; }
        }

        public void ShowSettings()
        {
            MainContainer.Children.Clear();
            SettingsView settingsView = new SettingsView();
            settingsView.BackToChatRequested += ShowMain;
            settingsView.LogoutRequested += LogoutAsync;
            MainContainer.Children.Add(settingsView);
        }

        private void LoadLightTheme()
        {
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new System.Uri("Resources/LightTheme.xaml", System.UriKind.Relative) });
        }
    }

    public static class MessageBox
    {
        private static readonly object ToastLock = new();
        private static readonly List<NovaToastWindow> OpenToasts = [];

        public static System.Windows.MessageBoxResult Show(string messageBoxText) => Show(null, messageBoxText, "NovaChat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK);
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption) => Show(null, messageBoxText, caption, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK);
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button) => Show(null, messageBoxText, caption, button, System.Windows.MessageBoxImage.None, GetDefault(button));
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon) => Show(null, messageBoxText, caption, button, icon, GetDefault(button));
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult) => Show(null, messageBoxText, caption, button, icon, defaultResult);
        public static System.Windows.MessageBoxResult Show(Window owner, string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon) => Show(owner, messageBoxText, caption, button, icon, GetDefault(button));

        public static System.Windows.MessageBoxResult Show(Window? owner, string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            if (button == System.Windows.MessageBoxButton.OK)
            {
                ShowToast(owner, caption, messageBoxText, icon);
                return System.Windows.MessageBoxResult.OK;
            }

            return ShowConfirmation(owner, caption, messageBoxText, button, icon, defaultResult);
        }

        private static void ShowToast(Window? owner, string caption, string message, System.Windows.MessageBoxImage icon)
        {
            if (Application.Current?.Dispatcher == null) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new NovaToastWindow(owner, caption, message, icon);
                lock (ToastLock)
                {
                    OpenToasts.Add(toast);
                    toast.Closed += (_, _) =>
                    {
                        lock (ToastLock)
                        {
                            OpenToasts.Remove(toast);
                            RepositionToasts();
                        }
                    };
                    toast.SetSlot(OpenToasts.Count - 1);
                }
                toast.Show();
            });
        }

        private static void RepositionToasts()
        {
            for (var i = 0; i < OpenToasts.Count; i++) OpenToasts[i].SetSlot(i);
        }

        private static System.Windows.MessageBoxResult ShowConfirmation(Window? owner, string caption, string message, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            System.Windows.MessageBoxResult result = defaultResult;
            var dialog = new NovaConfirmWindow(owner, caption, message, button, icon, defaultResult);
            dialog.ResultSelected += selected => result = selected;
            dialog.ShowDialog();
            return result;
        }

        private static System.Windows.MessageBoxResult GetDefault(System.Windows.MessageBoxButton button) => button switch
        {
            System.Windows.MessageBoxButton.YesNo => System.Windows.MessageBoxResult.No,
            System.Windows.MessageBoxButton.OKCancel => System.Windows.MessageBoxResult.Cancel,
            System.Windows.MessageBoxButton.YesNoCancel => System.Windows.MessageBoxResult.Cancel,
            _ => System.Windows.MessageBoxResult.OK
        };
    }

    internal sealed class NovaToastWindow : Window
    {
        private readonly Window? _owner;
        private readonly DispatcherTimer _timer;
        private readonly TranslateTransform _translate = new(40, 0);
        private int _slot;
        private bool _isClosing;

        public NovaToastWindow(Window? owner, string title, string message, System.Windows.MessageBoxImage icon)
        {
            _owner = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
            if (_owner != null) Owner = _owner;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 170;
            MinHeight = 92;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = false;
            Opacity = 0;

            var accentKey = icon switch
            {
                System.Windows.MessageBoxImage.Error => "DangerBrush",
                System.Windows.MessageBoxImage.Warning => "WarningBrush",
                System.Windows.MessageBoxImage.Information => "InfoBrush",
                _ => "PrimaryBrush"
            };

            var panel = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 12, 14),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.22 },
                RenderTransform = _translate,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            panel.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
            panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top };
            badge.SetResourceReference(Border.BackgroundProperty, "PrimarySoftBrush");
            var badgeText = new TextBlock { Text = IconText(icon), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, accentKey);
            badge.Child = badgeText;
            root.Children.Add(badge);

            var text = new StackPanel { Margin = new Thickness(12, 0, 10, 0) };
            var titleText = new TextBlock { Text = string.IsNullOrWhiteSpace(title) ? "NovaChat" : title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var messageText = new TextBlock { Text = message ?? string.Empty, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 88 };
            messageText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            text.Children.Add(titleText);
            text.Children.Add(messageText);
            Grid.SetColumn(text, 1);
            root.Children.Add(text);

            var close = new Button { Content = "×", Width = 30, Height = 30, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 20, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand };
            close.SetResourceReference(Control.ForegroundProperty, "SecondaryTextBrush");
            close.Click += (_, _) => CloseWithAnimation();
            Grid.SetColumn(close, 2);
            root.Children.Add(close);

            var accent = new Border { Width = 4, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), IsHitTestVisible = false };
            accent.SetResourceReference(Border.BackgroundProperty, accentKey);
            Grid.SetColumn(accent, 0);
            root.Children.Add(accent);

            panel.Child = root;
            Content = panel;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(icon == System.Windows.MessageBoxImage.Error ? 5.5 : icon == System.Windows.MessageBoxImage.Warning ? 5 : 3.8) };
            _timer.Tick += (_, _) => CloseWithAnimation();
            Closed += (_, _) => _timer.Stop();
            Loaded += (_, _) =>
            {
                PositionWindow();
                _timer.Start();
                var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                BeginAnimation(OpacityProperty, fade);
                var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                _translate.BeginAnimation(TranslateTransform.XProperty, slide);
            };
        }

        public void SetSlot(int slot)
        {
            _slot = slot;
            if (IsVisible) PositionWindow();
        }

        private void PositionWindow()
        {
            var anchor = _owner;
            if (anchor == null) return;
            UpdateLayout();
            Left = anchor.Left + Math.Max(0, anchor.ActualWidth - Width - 24);
            Top = anchor.Top + Math.Max(0, anchor.ActualHeight - ActualHeight - 24 - _slot * (ActualHeight + 10));
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;
            _timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
            var slide = new DoubleAnimation(24, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            _translate.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        private static string IconText(System.Windows.MessageBoxImage icon) => icon switch
        {
            System.Windows.MessageBoxImage.Error => "×",
            System.Windows.MessageBoxImage.Warning => "!",
            System.Windows.MessageBoxImage.Question => "?",
            _ => "i"
        };
    }

    internal sealed class NovaConfirmWindow : Window
    {
        public event Action<System.Windows.MessageBoxResult>? ResultSelected;

        public NovaConfirmWindow(Window? owner, string title, string message, System.Windows.MessageBoxButton buttons, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            if (owner != null) Owner = owner;
            else if (Application.Current?.MainWindow != null && Application.Current.MainWindow != this) Owner = Application.Current.MainWindow;
            Width = 500;
            Height = 305;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = true;

            var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(24), BorderThickness = new Thickness(1), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 35, ShadowDepth = 12, Opacity = 0.25 } };
            card.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(17) };
            badge.SetResourceReference(Border.BackgroundProperty, "PrimarySoftBrush");
            var badgeText = new TextBlock { Text = IconText(icon), FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, icon == System.Windows.MessageBoxImage.Error ? "DangerBrush" : icon == System.Windows.MessageBoxImage.Warning ? "WarningBrush" : "PrimaryBrush");
            badge.Child = badgeText;
            header.Children.Add(badge);
            var heading = new StackPanel { Margin = new Thickness(14, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var headingText = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold }; headingText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var subtitle = new TextBlock { Text = "Please confirm this action", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) }; subtitle.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            heading.Children.Add(headingText); heading.Children.Add(subtitle); header.Children.Add(heading);
            Grid.SetRow(header, 0); root.Children.Add(header);

            var separator = new Border { Height = 1, Margin = new Thickness(0, 18, 0, 14) }; separator.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); Grid.SetRow(separator, 1); root.Children.Add(separator);
            var body = new TextBlock { Text = message, FontSize = 13, LineHeight = 21, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top }; body.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); Grid.SetRow(body, 2); root.Children.Add(body);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            foreach (var spec in BuildButtonSpecs(buttons))
            {
                var button = new Button { Content = spec.Text, Width = spec.Width, Height = 40, Margin = new Thickness(spec.IsFirst ? 0 : 8, 0, 0, 0), Style = GetButtonStyle(spec.Result) };
                button.Click += (_, _) => { ResultSelected?.Invoke(spec.Result); DialogResult = spec.Result != System.Windows.MessageBoxResult.Cancel; };
                button.IsDefault = spec.Result == defaultResult;
                button.IsCancel = spec.Result == System.Windows.MessageBoxResult.Cancel;
                actions.Children.Add(button);
                if (button.IsDefault) button.Dispatcher.BeginInvoke(() => button.Focus());
            }
            Grid.SetRow(actions, 3); root.Children.Add(actions);

            card.Child = root;
            Content = card;

            KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                var escapeResult = buttons == System.Windows.MessageBoxButton.YesNo ? System.Windows.MessageBoxResult.No : System.Windows.MessageBoxResult.Cancel;
                ResultSelected?.Invoke(escapeResult);
                DialogResult = escapeResult != System.Windows.MessageBoxResult.Cancel;
            };
            Loaded += (_, _) => Opacity = 1;
        }

        private Style? GetButtonStyle(System.Windows.MessageBoxResult result) => Application.Current?.FindResource(result is System.Windows.MessageBoxResult.Yes or System.Windows.MessageBoxResult.OK ? "PrimaryButtonStyle" : result == System.Windows.MessageBoxResult.No ? "SecondaryButtonStyle" : "SecondaryButtonStyle") as Style;

        private static IEnumerable<(string Text, System.Windows.MessageBoxResult Result, bool IsFirst, double Width)> BuildButtonSpecs(System.Windows.MessageBoxButton button) => button switch
        {
            System.Windows.MessageBoxButton.YesNo => [("No", System.Windows.MessageBoxResult.No, true, 92), ("Yes", System.Windows.MessageBoxResult.Yes, false, 108)],
            System.Windows.MessageBoxButton.OKCancel => [("Cancel", System.Windows.MessageBoxResult.Cancel, true, 98), ("OK", System.Windows.MessageBoxResult.OK, false, 98)],
            System.Windows.MessageBoxButton.YesNoCancel => [("Cancel", System.Windows.MessageBoxResult.Cancel, true, 98), ("No", System.Windows.MessageBoxResult.No, false, 92), ("Yes", System.Windows.MessageBoxResult.Yes, false, 108)],
            _ => [("OK", System.Windows.MessageBoxResult.OK, true, 108)]
        };

        private static string IconText(System.Windows.MessageBoxImage icon) => icon switch
        {
            System.Windows.MessageBoxImage.Error => "×",
            System.Windows.MessageBoxImage.Warning => "!",
            System.Windows.MessageBoxImage.Question => "?",
            _ => "i"
        };
    }
}