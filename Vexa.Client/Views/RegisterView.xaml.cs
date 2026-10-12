using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private bool _usernameTaken;
        private bool _emailTaken;
        private bool _phoneTaken;

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
            _apiService = new ApiService();
            Unloaded += RegisterView_Unloaded;
            UpdateRegisterButtonState();
        }

        private void RegisterView_Unloaded(object sender, RoutedEventArgs e)
        {
            var pendingCheck = _availabilityCts;
            _availabilityCts = null;
            pendingCheck?.Cancel();
            pendingCheck?.Dispose();
        }

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            _usernameTaken = false;
            UsernameDuplicateText.Visibility = Visibility.Collapsed;
            UpdateRegisterButtonState();
            await CheckAvailabilityAsync();
        }

        private async void EmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            _emailTaken = false;
            EmailDuplicateText.Visibility = Visibility.Collapsed;
            UpdateRegisterButtonState();
            await CheckAvailabilityAsync();
        }

        private async void PhoneNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            _phoneTaken = false;
            PhoneDuplicateText.Visibility = Visibility.Collapsed;
            PhoneHintText.Visibility = string.IsNullOrEmpty(PhoneNumberTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ValidatePhoneNumber();
            UpdateRegisterButtonState();
            await CheckAvailabilityAsync();
        }

        private void DisplayNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            UpdateRegisterButtonState();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRegistrationStatusReset)
                return;

            HideRegistrationStatus();
            UpdateRegisterButtonState();
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
            phone.Length == 11 && phone[0] == '0' && phone.All(ch => ch >= '0' && ch <= '9');

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

        private void UpdateRegisterButtonState()
        {
            if (RegisterButton == null)
                return;

            var username = UsernameTextBox.Text.Trim();
            var displayName = DisplayNameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var phone = PhoneNumberTextBox.Text.Trim();
            var password = PasswordBox.Password;

            var locallyValid =
                username.Length >= 3 &&
                username.Length <= 32 &&
                !string.IsNullOrWhiteSpace(displayName) &&
                IsValidEmail(email) &&
                IsValidPhoneNumber(phone) &&
                !string.IsNullOrWhiteSpace(password);

            RegisterButton.IsEnabled =
                Volatile.Read(ref _registrationInProgress) == 0 &&
                locallyValid &&
                !_usernameTaken &&
                !_emailTaken &&
                !_phoneTaken;
        }

        private static string GetProposedText(TextBox box, string insertedText)
        {
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            return box.Text.Remove(start, length).Insert(start, insertedText);
        }

        private async Task CheckAvailabilityAsync()
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

            // Don't contact the server for empty or obviously incomplete fields.
            var usernameToCheck = username.Length is >= 3 and <= 32 ? username : string.Empty;
            var emailToCheck = email.Contains('@') ? email : string.Empty;
            var phoneToCheck = IsValidPhoneNumber(phone) ? phone : string.Empty;

            if (string.IsNullOrWhiteSpace(usernameToCheck) &&
                string.IsNullOrWhiteSpace(emailToCheck) &&
                string.IsNullOrWhiteSpace(phoneToCheck))
            {
                UpdateRegisterButtonState();
                return;
            }

            try
            {
                // Debounce typing, and pass the same token to HttpClient so superseded
                // availability checks are cancelled instead of running in the background.
                await Task.Delay(400, token);

                var query =
                    $"api/User/registration-availability?username={Uri.EscapeDataString(usernameToCheck)}" +
                    $"&email={Uri.EscapeDataString(emailToCheck)}" +
                    $"&phoneNumber={Uri.EscapeDataString(phoneToCheck)}";

                var result = await _apiService.GetAsync<RegistrationAvailabilityResponse>(query, token);
                if (token.IsCancellationRequested || result == null)
                    return;

                _usernameTaken = !string.IsNullOrWhiteSpace(usernameToCheck) && result.UsernameTaken;
                _emailTaken = !string.IsNullOrWhiteSpace(emailToCheck) && result.EmailTaken;
                _phoneTaken = !string.IsNullOrWhiteSpace(phoneToCheck) && result.PhoneTaken;

                UsernameDuplicateText.Visibility = _usernameTaken ? Visibility.Visible : Visibility.Collapsed;
                EmailDuplicateText.Visibility = _emailTaken ? Visibility.Visible : Visibility.Collapsed;
                PhoneDuplicateText.Visibility = _phoneTaken ? Visibility.Visible : Visibility.Collapsed;
                UpdateRegisterButtonState();
            }
            catch (OperationCanceledException)
            {
                // Expected when the user continues typing or leaves the page.
            }
            catch (HttpRequestException)
            {
                // Availability is only a convenience check; the server still validates
                // uniqueness on registration. Don't lock the form if the server is busy.
                if (!token.IsCancellationRequested)
                    UpdateRegisterButtonState();
            }
            catch
            {
                if (!token.IsCancellationRequested)
                    UpdateRegisterButtonState();
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _registrationInProgress, 1) == 1)
                return;

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
                RegisterButton.Content = "Creating account…";

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

                var result = await _apiService.PostAsync<RegisterRequest, RegisterResponse>("api/User/register", request);
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
                _usernameTaken = false;
                _emailTaken = false;
                _phoneTaken = false;
                UsernameDuplicateText.Visibility = Visibility.Collapsed;
                EmailDuplicateText.Visibility = Visibility.Collapsed;
                PhoneDuplicateText.Visibility = Visibility.Collapsed;
                PhoneHintText.Visibility = Visibility.Visible;
                ValidatePhoneNumber();
                UpdateRegisterButtonState();
                ShowRegistrationSuccess("Your Vexa account is ready. Return to sign in whenever you're ready.");
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
            catch (Exception)
            {
                ShowRegistrationError("Vexa could not be reached. Check that the server is running and try again.");
            }
            finally
            {
                Interlocked.Exchange(ref _registrationInProgress, 0);
                RegisterButton.Content = "Create account";
                UpdateRegisterButtonState();
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
