using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    private static readonly bool DirectAvatarHandlerRegistered = RegisterDirectAvatarHandler();

    private static bool RegisterDirectAvatarHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnProfileLoadedForDirectAvatar));
        return true;
    }

    private static void OnProfileLoadedForDirectAvatar(object sender, RoutedEventArgs e)
    {
        if (sender is ProfileView view)
            view.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => _ = view.RenderDirectAvatarAsync()));
    }

    private async Task RenderDirectAvatarAsync()
    {
        if (!AuthState.IsAuthenticated || string.IsNullOrWhiteSpace(AuthState.UserId)) return;

        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>("api/User/profile/me");
            if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

            var url = _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            var separator = url.Contains('?') ? '&' : '?';
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(
                $"{url}{separator}v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            await Dispatcher.InvokeAsync(() =>
            {
                AvatarImage.Source = bitmap;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarInitialsText.Visibility = Visibility.Collapsed;
            });
        }
        catch
        {
            // Keep the normal initials fallback if the file is unavailable.
        }
    }
}
