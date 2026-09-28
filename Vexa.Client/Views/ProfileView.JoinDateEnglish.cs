using System.Globalization;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    static ProfileView()
    {
        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnProfileLoadedForEnglishDate));
    }

    private static void OnProfileLoadedForEnglishDate(object sender, RoutedEventArgs e)
    {
        if (sender is not ProfileView view) return;

        void Apply()
        {
            if (view.DataContext is NovaChat.Client.Models.ProfileModel profile && view.JoinedText != null)
                view.JoinedText.Text = $"Joined {profile.CreatedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        view.DataContextChanged -= OnProfileDataContextChanged;
        view.DataContextChanged += OnProfileDataContextChanged;
        view.Dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private static void OnProfileDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ProfileView view) return;
        if (view.DataContext is NovaChat.Client.Models.ProfileModel profile && view.JoinedText != null)
            view.JoinedText.Text = $"Joined {profile.CreatedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
    }
}
