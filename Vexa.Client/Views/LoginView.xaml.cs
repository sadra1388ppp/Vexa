using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        private readonly ApiService _apiService;
        private bool _loginInProgress;

        private static readonly Brush StatusBrush = new SolidColorBrush(Color.FromRgb(170, 183, 197));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 135, 135));

        public event Action? CreateAccountRequested;
        public event Action? LoginSuccessful;
        public event Action? OwnerLoginSuccessful;

        public LoginView()
        {
            InitializeComponent();
            _apiService = new ApiService();
            var environmentName =
                Environment.GetEnvironmentVariable("VEXA_ENVIRONMENT") ??
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                "Development";
            LoginEnvironmentText.Text = environmentName.ToUpperInvariant();
            Loaded += (_, _) => UserIdTextBox.Focus();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loginInProgress)
                return;

            var login = UserIdTextBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                SetStatus("Enter your username, email or phone number and password.", isError: true);
                if (string.IsNullOrWhiteSpace(login))
                    UserIdTextBox.Focus();
                else
                    PasswordBox.Focus();
                return;
            }

            _loginInProgress = true;
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Signing in…";
            SetStatus("Connecting securely…");

            try
            {
                var request = new LoginRequest { Id = login, Password = password };
                var result = await _apiService.PostAsync<LoginRequest, LoginResponse>("api/User/login", request);

                if (result == null || string.IsNullOrWhiteSpace(result.Token) || result.User == null)
                {
                    SetStatus("We couldn't sign you in. Check your details and try again.", isError: true);
                    return;
                }

                AuthState.Set(
                    result.Token,
                    result.User.Id,
                    result.User.Username,
                    result.User.DisplayName,
                    result.User.Email);

                // Keep the server as the authority for OwnerOnly access.
                var isOwner = false;
                try
                {
                    await _apiService.GetAsync<OwnerAccessResponse>("api/Admin/test");
                    isOwner = true;
                }
                catch (HttpRequestException)
                {
                    isOwner = false;
                }

                SetStatus(string.Empty);
                if (isOwner)
                    OwnerLoginSuccessful?.Invoke();
                else
                    LoginSuccessful?.Invoke();
            }
            catch (HttpRequestException ex)
            {
                var unauthorized =
                    ex.Message.Contains("(401 Unauthorized)", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("Invalid username/phone number or password", StringComparison.OrdinalIgnoreCase);

                SetStatus(
                    unauthorized
                        ? "Incorrect username, email/phone number or password."
                        : "Vexa couldn't be reached. Check that the server is running and try again.",
                    isError: true);
            }
            catch (Exception)
            {
                SetStatus("Something went wrong while signing in. Please try again.", isError: true);
            }
            finally
            {
                _loginInProgress = false;
                LoginButton.Content = "Sign in";
                LoginButton.IsEnabled = true;
            }
        }

        private void SetStatus(string message, bool isError = false)
        {
            LoginStatusTextBlock.Text = message;
            LoginStatusTextBlock.Foreground = isError ? ErrorBrush : StatusBrush;
            LoginStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAccountRequested?.Invoke();
        }

        private sealed class OwnerAccessResponse
        {
            public string? Message { get; set; }
        }
    }
}
