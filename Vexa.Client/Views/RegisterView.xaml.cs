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

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
            _apiService = new ApiService();
        }

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressRegistrationStatusReset)
                HideRegistrationStatus();
            await CheckAvailabilityAsync();
        }

        private async void EmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressRegistrationStatusReset)
                HideRegistrationStatus();
            await CheckAvailabilityAsync();
        }

        private async void PhoneNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressRegistrationStatusReset)
                HideRegistrationStatus();
            PhoneHintText.Visibility = string.IsNullOrEmpty(PhoneNumberTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ValidatePhoneNumber();
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
            var valid = phone.Length == 11 && phone[0] == '0' && phone.All(ch => ch >= '0' && ch <= '9');
            PhoneValidationText.Visibility = string.IsNullOrWhiteSpace(phone) || valid ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string GetProposedText(TextBox box, string insertedText)
        {
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            return box.Text.Remove(start, length).Insert(start, insertedText);
        }

        private async System.Threading.Tasks.Task CheckAvailabilityAsync()
        {
            _availabilityCts?.Cancel();
            _availabilityCts?.Dispose();
            _availabilityCts = new CancellationTokenSource();
            var token = _availabilityCts.Token;

            var username = UsernameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var phone = PhoneNumberTextBox.Text.Trim();

            UsernameDuplicateText.Visibility = Visibility.Collapsed;
            EmailDuplicateText.Visibility = Visibility.Collapsed;
            PhoneDuplicateText.Visibility = Visibility.Collapsed;

            try
            {
                await System.Threading.Tasks.Task.Delay(350, token);
                if (token.IsCancellationRequested)
                    return;

                var query = $"api/User/registration-availability?username={Uri.EscapeDataString(username)}&email={Uri.EscapeDataString(email)}&phoneNumber={Uri.EscapeDataString(phone)}";
                var result = await _apiService.GetAsync<RegistrationAvailabilityResponse>(query);
                if (token.IsCancellationRequested || result == null)
                    return;

                UsernameDuplicateText.Visibility = result.UsernameTaken ? Visibility.Visible : Visibility.Collapsed;
                EmailDuplicateText.Visibility = result.EmailTaken ? Visibility.Visible : Visibility.Collapsed;
                PhoneDuplicateText.Visibility = result.PhoneTaken ? Visibility.Visible : Visibility.Collapsed;
                RegisterButton.IsEnabled = !result.UsernameTaken && !result.EmailTaken && !result.PhoneTaken;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
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
                ShowRegistrationSuccess("Your NovaChat account is ready. You can create another account or return to sign in.");
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
