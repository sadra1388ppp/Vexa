using System.Windows;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private string GetRegistrationMessagePrivacy()
    {
        return RegistrationMessageEverybodyRadio?.IsChecked == true
            ? "Everybody"
            : "Requests";
    }

    private bool GetRegistrationAllowGroupAdds() => RegistrationAllowGroupAddsBox?.IsChecked != false;

    private void RegistrationMessagePrivacyChanged(object sender, RoutedEventArgs e)
    {
        if (RegistrationMessageEverybodyRadio?.IsChecked == true)
        {
            if (RegistrationMessagePrivacyDescription != null)
                RegistrationMessagePrivacyDescription.Text = "Anyone can start a conversation with you directly.";

            if (RegistrationMessagePrivacyStatus != null)
                RegistrationMessagePrivacyStatus.Text = "Everyone can message you";
        }
        else if (RegistrationMessageRequestsRadio?.IsChecked == true)
        {
            if (RegistrationMessagePrivacyDescription != null)
                RegistrationMessagePrivacyDescription.Text = "New conversations arrive as requests until you approve them.";

            if (RegistrationMessagePrivacyStatus != null)
                RegistrationMessagePrivacyStatus.Text = "New conversations require approval";
        }
    }
}
