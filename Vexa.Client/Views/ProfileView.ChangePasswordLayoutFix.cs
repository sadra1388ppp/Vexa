using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    private static readonly bool ChangePasswordWindowLayoutFixRegistered = RegisterChangePasswordWindowLayoutFix();

    private static bool RegisterChangePasswordWindowLayoutFix()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoadedForChangePassword));
        return true;
    }

    private static void OnAnyWindowLoadedForChangePassword(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !string.Equals(window.Title, "Change password", StringComparison.Ordinal))
            return;

        window.Width = 460;
        window.Height = 500;
        window.MinHeight = 500;
        window.MaxHeight = 700;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;

        if (window.Content is Panel root)
        {
            root.Margin = new Thickness(24);
        }
    }
}
