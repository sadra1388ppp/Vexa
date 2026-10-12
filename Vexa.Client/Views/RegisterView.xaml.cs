using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class RegisterView : UserControl
    {
        private readonly ApiService _apiService;
        private int _registrationInProgress;
        private CancellationTokenSource? _availabilityCts;
        private bool _suppressRegistrationStatusReset;
        private readonly CancellationTokenSource _viewCts = new();
        private bool _radialBackdropCached;
        private Border? _registrationShadowSurface;

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
            _apiService = new ApiService();
            Loaded += RegisterView_Loaded;
            Unloaded += RegisterView_Unloaded;
        }

        private void RegisterView_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewCts.Cancel();

            var pendingCheck = _availabilityCts;
            _availabilityCts = null;
            pendingCheck?.Cancel();
            pendingCheck?.Dispose();
        }

        private void RegisterView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_radialBackdropCached)
            {
                _radialBackdropCached = true;
                CacheStaticRadialBackdrops(this);
            }

            IsolateRegistrationCardShadow();
        }

        private void IsolateRegistrationCardShadow()
        {
            if (_registrationShadowSurface != null)
                return;

            var card = FindShadowedBorder(this);
            if (card == null || card.Effect is not DropShadowEffect shadowEffect ||
                VisualTreeHelper.GetParent(card) is not Panel parent)
                return;

            int index = parent.Children.IndexOf(card);
            if (index < 0)
                return;

            // Keep the same card surface and shadow, but render the static surface
            // separately. TextBox updates should not invalidate a shadow containing
            // the entire live registration form.
            var shadowSurface = new Border
            {
                Width = card.Width,
                Height = card.ActualHeight > 0 ? card.ActualHeight : card.DesiredSize.Height,
                MinWidth = card.MinWidth,
                MaxWidth = card.MaxWidth,
                HorizontalAlignment = card.HorizontalAlignment,
                VerticalAlignment = card.VerticalAlignment,
                Margin = card.Margin,
                BorderThickness = card.BorderThickness,
                CornerRadius = card.CornerRadius,
                Background = card.Background,
                BorderBrush = card.BorderBrush,
                Effect = (DropShadowEffect)shadowEffect.CloneCurrentValue(),
                CacheMode = new BitmapCache(),
                IsHitTestVisible = false,
                Focusable = false,
                SnapsToDevicePixels = card.SnapsToDevicePixels
            };

            parent.Children.Insert(index, shadowSurface);

            // Leave the live form, all fields, padding and sizes where they are.
            // The sibling behind it now draws the original translucent surface.
            card.Effect = null;
            card.Background = Brushes.Transparent;
            card.BorderBrush = Brushes.Transparent;
            card.SizeChanged += RegistrationCard_SizeChanged;

            _registrationShadowSurface = shadowSurface;
        }

        private static Border? FindShadowedBorder(DependencyObject parent)
        {
            if (parent is Border border && border.Effect is DropShadowEffect)
                return border;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var found = FindShadowedBorder(VisualTreeHelper.GetChild(parent, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        private void RegistrationCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_registrationShadowSurface == null)
                return;

            if (Math.Abs(_registrationShadowSurface.ActualHeight - e.NewSize.Height) > 0.5)
                _registrationShadowSurface.Height = e.NewSize.Height;
            if (Math.Abs(_registrationShadowSurface.ActualWidth - e.NewSize.Width) > 0.5)
                _registrationShadowSurface.Width = e.NewSize.Width;
        }

        private static void CacheStaticRadialBackdrops(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // The page's large decorative gradient is static. Cache only that
                // layer; never cache the editable form controls themselves.
                if (child is Border border &&
                    border.Background is RadialGradientBrush &&
                    border.CacheMode == null)
                {
                    border.CacheMode = new BitmapCache();
                }

                CacheStaticRadialBackdrops(child);
            }
        }

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            await CheckAvailabilityAsync();
        }

        private async void EmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            await CheckAvailabilityAsync();
        }

        private async void PhoneNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressRegistrationStatusReset)
                HideRegistrationStatus();

            PhoneHintText.Visibility = string.IsNullOrEmpty(PhoneNumberTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ValidatePhoneNumber();

            if (_suppressRegistrationStatusReset)
                return;

            await CheckAvailabilityAsync();
        }

        private void PhoneNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => ch < '0' || ch > '9'))
            {
                e.Handled = true;
                return;
            }

            var proposed = GetProposedText(PhoneNumberTextBox, e.Text);
            if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0'))
                e.Handled = true;
        }

        private void PhoneNumberTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            var pasted = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            var proposed = GetProposedText(PhoneNumberTextBox, pasted);
            if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0') || proposed.Any(ch => ch < '0' || ch > '9'))
                e.CancelCommand();
        }

        private void ValidatePhoneNumber()
        {
            var phone = PhoneNumberTextBox.Text.Trim();
            PhoneValidationText.Visibility = string.IsNullOrWhiteSpace(phone) || IsValidPhoneNumber(phone)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static bool IsValidPhoneNumber(string phone) =>
            phone.Length == 11 &&
            phone[0] == '0' &&
            phone.All(ch => ch >= '0' && ch <= '9');

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var address = new System.Net.Mail.MailAddress(email);
                return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string GetProposedText(TextBox box, string insertedText)
        {
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            return box.Text.Remove(start, length).Insert(start, insertedText);
        }

        private async System.Threading.Tasks.Task CheckAvailabilityAsync()
        {
            var previousCheck = _availabilityCts;
            var currentCheck = new CancellationTokenSource();
            _availabilityCts = currentCheck;
            previousCheck?.Cancel();
            previousCheck?.Dispose();

            var token = currentCheck.Token;
            var username = UsernameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var phone = PhoneNumberTextBox.Text.Trim();

            UsernameDuplicateText.Visibility = Visibility.Collapsed;
            EmailDuplicateText.Visibility = Visibility.Collapsed;
            PhoneDuplicateText.Visibility = Visibility.Collapsed;

            // Avoid network calls for values that cannot yet be submitted.
            // The endpoint only receives fields that are locally complete.
            var usernameToCheck = username.Length is >= 3 and <= 32 ? username : string.Empty;
            var emailToCheck = IsValidEmail(email) ? email : string.Empty;
            var phoneToCheck = IsValidPhoneNumber(phone) ? phone : string.Empty;

            if (string.IsNullOrWhiteSpace(usernameToCheck) &&
                string.IsNullOrWhiteSpace(emailToCheck) &&
                string.IsNullOrWhiteSpace(phoneToCheck))
            {
                return;
            }

            try
            {
                // A short debounce avoids one availability request per keystroke.
                await System.Threading.Tasks.Task.Delay(350, token);

                var query =
                    $"api/User/registration-availability?username={Uri.EscapeDataString(usernameToCheck)}" +
                    $"&email={Uri.EscapeDataString(emailToCheck)}" +
                    $"&phoneNumber={Uri.EscapeDataString(phoneToCheck)}";

                var result = await _apiService.GetAsync<RegistrationAvailabilityResponse>(query, token);
                if (token.IsCancellationRequested || result == null)
                    return;

                UsernameDuplicateText.Visibility =
                    !string.IsNullOrWhiteSpace(usernameToCheck) && result.UsernameTaken
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                EmailDuplicateText.Visibility =
                    !string.IsNullOrWhiteSpace(emailToCheck) && result.EmailTaken
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                PhoneDuplicateText.Visibility =
                    !string.IsNullOrWhiteSpace(phoneToCheck) && result.PhoneTaken
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                RegisterButton.IsEnabled =
                    !( (!string.IsNullOrWhiteSpace(usernameToCheck) && result.UsernameTaken) ||
                       (!string.IsNullOrWhiteSpace(emailToCheck) && result.EmailTaken) ||
                       (!string.IsNullOrWhiteSpace(phoneToCheck) && result.PhoneTaken) );
            }
            catch (OperationCanceledException)
            {
                // Expected when the user types again or leaves the registration page.
            }
            catch (HttpRequestException)
            {
                // Availability is advisory; registration performs final server validation.
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _registrationInProgress, 1) == 1)
                return;

            // Registration is the final uniqueness check; cancel any advisory
            // request so it cannot compete with or overwrite the submit flow.
            var pendingAvailability = _availabilityCts;
            _availabilityCts = null;
            pendingAvailability?.Cancel();
            pendingAvailability?.Dispose();

            var username = UsernameTextBox.Text.Trim();
            var displayName = DisplayNameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var phoneNumber = PhoneNumberTextBox.Text.Trim();
            var password = PasswordBox.Password;

            HideRegistrationStatus();

            try
            {
                if (string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrWhiteSpace(displayName) ||
                    string.IsNullOrWhiteSpace(email) ||
                    string.IsNullOrWhiteSpace(phoneNumber) ||
                    string.IsNullOrWhiteSpace(password))
                {
                    ShowRegistrationError("Please fill in all fields.");
                    return;
                }

                if (username.Length < 3 || username.Length > 32)
                {
                    ShowRegistrationError("Username must be 3 to 32 characters.");
                    return;
                }

                if (phoneNumber.Length != 11 || phoneNumber[0] != '0' || !phoneNumber.All(ch => ch >= '0' && ch <= '9'))
                {
                    ShowRegistrationError("Phone number must contain exactly 11 digits and start with 0.");
                    return;
                }

                RegisterButton.IsEnabled = false;

                var request = new RegisterRequest
                {
                    Username = username,
                    DisplayName = displayName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    Password = password,
                    MessagePrivacy = GetRegistrationMessagePrivacy(),
                    AllowGroupAdds = GetRegistrationAllowGroupAdds()
                };

                var result = await _apiService.PostAsync<RegisterRequest, RegisterResponse>(
                    "api/User/register", request, _viewCts.Token);
                if (result == null)
                {
                    ShowRegistrationError("Registration failed. Please check the entered information and try again.");
                    return;
                }

                if (!result.Message.Contains("success", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRegistrationError(result.Message);
                    return;
                }

                _suppressRegistrationStatusReset = true;
                try
                {
                    PasswordBox.Clear();
                    UsernameTextBox.Clear();
                    DisplayNameTextBox.Clear();
                    EmailTextBox.Clear();
                    PhoneNumberTextBox.Clear();
                    RegistrationMessageEverybodyRadio.IsChecked = true;
                    RegistrationAllowGroupAddsBox.IsChecked = true;
                }
                finally
                {
                    _suppressRegistrationStatusReset = false;
                }

                RegistrationMessagePrivacyChanged(this, new RoutedEventArgs());
                ShowRegistrationSuccess("Your NovaChat account is ready. You can create another account or return to sign in.");
            }
            catch (OperationCanceledException) when (_viewCts.IsCancellationRequested)
            {
                // The registration page was closed; stop its outstanding request.
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("(409 Conflict)", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRegistrationError(ExtractApiMessage(ex.Message));
                    await CheckAvailabilityAsync();
                    return;
                }

                ShowRegistrationError(ex.Message);
            }
            catch (Exception ex)
            {
                ShowRegistrationError($"Could not connect to the server.\n\n{ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _registrationInProgress, 0);
                RegisterButton.IsEnabled = true;
            }
        }

        private void ShowRegistrationError(string message)
        {
            RegistrationSuccessCard.Visibility = Visibility.Collapsed;
            RegistrationErrorTextBlock.Text = message;
            RegistrationErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void ShowRegistrationSuccess(string message)
        {
            RegistrationErrorTextBlock.Text = string.Empty;
            RegistrationErrorTextBlock.Visibility = Visibility.Collapsed;
            RegistrationSuccessTextBlock.Text = message;
            RegistrationSuccessCard.Visibility = Visibility.Visible;
        }

        private void HideRegistrationStatus()
        {
            RegistrationErrorTextBlock.Text = string.Empty;
            RegistrationErrorTextBlock.Visibility = Visibility.Collapsed;
            RegistrationSuccessTextBlock.Text = string.Empty;
            RegistrationSuccessCard.Visibility = Visibility.Collapsed;
        }

        private static string ExtractApiMessage(string exceptionMessage)
        {
            const string marker = "): ";
            var index = exceptionMessage.IndexOf(marker, StringComparison.Ordinal);
            return index >= 0 && index + marker.Length < exceptionMessage.Length
                ? exceptionMessage[(index + marker.Length)..].Trim()
                : exceptionMessage;
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (Volatile.Read(ref _registrationInProgress) == 1)
                return;

            BackToLoginRequested?.Invoke();
        }

        private sealed class RegistrationAvailabilityResponse
        {
            public bool UsernameTaken { get; set; }
            public bool EmailTaken { get; set; }
            public bool PhoneTaken { get; set; }
        }
    }
}
