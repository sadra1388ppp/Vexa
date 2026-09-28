using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Net.Http;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    public async Task OpenChatWithUsernameAsync(string username)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return;

        if (!AuthState.IsAuthenticated)
        {
            MessageBox.Show("Your session has expired. Please sign in again.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>(
                "api/Chat",
                new CreateChatRequest { Username = username });

            if (result?.Chat == null)
            {
                MessageBox.Show(
                    string.IsNullOrWhiteSpace(result?.Message) ? "The chat could not be created." : result.Message,
                    "NovaChat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            await LoadChatsAsync();

            var targetChat = _chats.FirstOrDefault(c =>
                string.Equals(c.Chat.User1Id, result.Chat.User1Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Chat.User2Id, result.Chat.User2Id, StringComparison.OrdinalIgnoreCase));

            if (targetChat == null)
            {
                targetChat = _chats.FirstOrDefault(c =>
                    string.Equals(c.Chat.OtherUserId(AuthState.UserId), result.Chat.User1Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Chat.OtherUserId(AuthState.UserId), result.Chat.User2Id, StringComparison.OrdinalIgnoreCase));
            }

            if (targetChat == null)
            {
                MessageBox.Show("The chat was created, but it could not be loaded into the chat list.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await OpenChatAsync(targetChat.Chat);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(ex.Message, "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the chat.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
