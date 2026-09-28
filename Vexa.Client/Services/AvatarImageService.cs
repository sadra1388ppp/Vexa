using System.Windows.Media.Imaging;

namespace NovaChat.Client.Services;

public sealed class AvatarImageService
{
    private readonly ApiService _apiService;

    public AvatarImageService(ApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<BitmapImage?> LoadAsync(string userId, string? avatarUrl = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        var endpoint = $"api/avatar/{Uri.EscapeDataString(userId.Trim())}?v={Uri.EscapeDataString(avatarUrl)}";
        try
        {
            var bytes = await _apiService.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0)
                return null;

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
