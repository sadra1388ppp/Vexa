using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaChat.Client.Views
{
    public partial class LoginView
    {
        static LoginView()
        {
            EventManager.RegisterClassHandler(
                typeof(LoginView),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnLoginViewLoadedForSubtitleFix));
        }

        private static void OnLoginViewLoadedForSubtitleFix(object sender, RoutedEventArgs e)
        {
            if (sender is not LoginView view)
                return;

            view.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (view.WelcomeSubText.Parent is Panel oldParent)
                    oldParent.Children.Remove(view.WelcomeSubText);

                if (!view.LoginRoot.Children.Contains(view.WelcomeSubText))
                    view.LoginRoot.Children.Add(view.WelcomeSubText);

                view.WelcomeSubText.HorizontalAlignment = HorizontalAlignment.Center;
                view.WelcomeSubText.VerticalAlignment = VerticalAlignment.Bottom;
                view.WelcomeSubText.Margin = new Thickness(0, 0, 0, 18);
            }), DispatcherPriority.Loaded);
        }
    }
}
