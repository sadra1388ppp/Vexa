using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Services;

public sealed class AvatarUrlConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var absoluteUrl = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
                ? absolute.ToString()
                : new Uri(new Uri("http://localhost:5256/"), url.TrimStart('/')).ToString();

            var separator = absoluteUrl.Contains('?') ? "&" : "?";
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"{absoluteUrl}{separator}v={DateTime.UtcNow.Ticks}");
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
