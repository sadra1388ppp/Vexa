using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async Task RefreshConversationAvatarsAsync()
    {
        var changed = false;

        foreach (var item in _chats.ToArray())
        {
            string? avatarEndpoint = null;

            if (item.Chat.IsGroup)
            {
                if (!string.IsNullOrWhiteSpace(item.Chat.AvatarUrl))
                    avatarEndpoint = $"api/Chat/{item.Chat.Id}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            }
            else
            {
                var userId = item.Chat.OtherUserId(AuthState.UserId);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    try
                    {
                        var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
                        if (profile != null)
                        {
                            item.DisplayName = profile.DisplayName;
                            avatarEndpoint = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                                ? null
                                : $"api/User/profile/{Uri.EscapeDataString(userId)}/avatar?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                            item.IsOnline = profile.IsOnline;
                        }
                    }
                    catch { }
                }
            }

            var absoluteKey = string.IsNullOrWhiteSpace(avatarEndpoint) ? null : _apiService.BuildAbsoluteUrl(avatarEndpoint);
            if (!string.Equals(item.AvatarUri, absoluteKey, StringComparison.OrdinalIgnoreCase))
            {
                item.AvatarUri = absoluteKey;
                changed = true;
                if (!string.IsNullOrWhiteSpace(avatarEndpoint))
                {
                    var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(avatarEndpoint));
                    item.AvatarSource = image;
                }
                else
                {
                    item.AvatarSource = null;
                }
            }
        }

        if (changed)
            RefreshChatsList();

        if (_currentChatId.HasValue && IsCurrentGroupChat && ChatHeaderAvatarImage.Visibility != Visibility.Visible)
            _ = RefreshCurrentGroupAvatarAsync();
    }

    private static async Task<BitmapImage?> LoadConversationAvatarAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        try
        {
            var absolute = Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
                ? parsed.ToString()
                : new ApiService().BuildAbsoluteUrl(endpoint);

            using var http = new HttpClient();
            if (!string.IsNullOrWhiteSpace(AuthState.Token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);

            using var response = await http.GetAsync(absolute, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;

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
        catch { return null; }
    }

    private void InitializeConversationAvatarFix()
    {
    }
}
