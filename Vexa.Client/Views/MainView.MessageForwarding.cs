using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private MessageModel? _forwardMessage;
    private string _forwardMessageContent = string.Empty;

    private async void ForwardMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || !_currentChatId.HasValue)
            return;

        try
        {
            var message = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId);
            if (message == null)
            {
                MessageBox.Show("The message could not be located.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (message.IsDeletedForEveryone)
            {
                MessageBox.Show("Deleted messages cannot be forwarded.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.Equals(message.MessageType, "text", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Forwarding currently supports text messages. Media forwarding will be added separately so encrypted files can be re-encrypted safely for each destination.",
                    "Forward Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var decrypted = await DecryptMessageWithRecoveryAsync(message);
            if (string.IsNullOrWhiteSpace(decrypted.Content) ||
                decrypted.Content.StartsWith("[Encrypted message", StringComparison.Ordinal))
            {
                MessageBox.Show("This message could not be decrypted on this device.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartForwardMode(message, decrypted.Content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not prepare the message for forwarding.\n\n{ex.Message}",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StartForwardMode(MessageModel message, string decryptedContent)
    {
        _forwardMessage = message;
        _forwardMessageContent = decryptedContent.Trim();
        ForwardPanel.Visibility = Visibility.Visible;
        MessageTextBox.Clear();
        MessageTextBox.ToolTip = "Add a comment (optional)";
        SendButton.Content = "Forward  ➤";

        ForwardSourceText.Text = string.IsNullOrWhiteSpace(message.SenderId)
            ? "Forwarded message"
            : $"@{message.SenderId}";

        ForwardSourceContentText.Text = _forwardMessageContent;
        ForwardTargetText.Text = _currentChatId.HasValue
            ? $"Forwarding to {(_chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value)?.DisplayName ?? "this conversation")}"
            : "Forwarding in the current conversation";
        ForwardModeHintText.Text = "Forwarding in this conversation";
        UpdateForwardUi();

        Dispatcher.BeginInvoke(() =>
        {
            MessageTextBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CancelForwardMode()
    {
        _forwardMessage = null;
        _forwardMessageContent = string.Empty;
        ForwardPanel.Visibility = Visibility.Collapsed;
        MessageTextBox.Clear();
        MessageTextBox.ToolTip = "Write a message";
        SendButton.Content = "Send  ➤";
        SendButton.IsEnabled = true;

        ForwardModeHintText.Text = string.Empty;
    }

    private void ForwardCancelButton_Click(object sender, RoutedEventArgs e) => CancelForwardMode();

    private async Task ForwardPendingMessageAsync()
    {
        if (_forwardMessage == null || string.IsNullOrWhiteSpace(_forwardMessageContent))
            return;

        if (!_currentChatId.HasValue)
        {
            ForwardModeHintText.Text = "Select a conversation first.";
            return;
        }

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            MessageBox.Show(
                "NovaChat is not connected to the server.",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var destinationChatId = _currentChatId.Value;
        var comment = MessageTextBox.Text.Trim();
        var forwardedText = BuildForwardedMessage(_forwardMessage, _forwardMessageContent, comment);

        SendButton.IsEnabled = false;
        ForwardCancelButton.IsEnabled = false;
        ForwardModeHintText.Text = "Forwarding securely...";

        try
        {
            var encrypted = await _e2ee.EncryptForChatAsync(
                destinationChatId,
                forwardedText,
                _apiService);

            await _hubConnection.InvokeAsync(
                "SendMessage",
                destinationChatId,
                encrypted);

            await LoadChatsAsync();
            System.Media.SystemSounds.Asterisk.Play();
            CancelForwardMode();
        }
        catch (Exception ex)
        {
            SendButton.IsEnabled = true;
            ForwardCancelButton.IsEnabled = true;
            ForwardModeHintText.Text = "Forwarding failed. Please try again.";
            MessageBox.Show(
                $"Could not forward the message.\n\n{ex.Message}",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildForwardedMessage(
        MessageModel sourceMessage,
        string decryptedContent,
        string comment)
    {
        var senderName = !string.IsNullOrWhiteSpace(sourceMessage.SenderName)
            ? sourceMessage.SenderName.Trim()
            : !string.IsNullOrWhiteSpace(sourceMessage.SenderId)
                ? $"@{sourceMessage.SenderId}"
                : "Unknown user";

        var forwarded = $"↗ {senderName}\n\n{decryptedContent.Trim()}";

        return string.IsNullOrWhiteSpace(comment)
            ? forwarded
            : $"{forwarded}\n\n\u200C{comment.Trim()}";
    }

    private void UpdateForwardUi()
    {
        SendButton.IsEnabled = _forwardMessage != null &&
                               _currentChatId.HasValue &&
                               _hubConnection?.State == HubConnectionState.Connected;

        ForwardModeHintText.Text = _forwardMessage == null
            ? string.Empty
            : "Forwarding in this conversation";
    }

    private Brush FindBrush(string key) => FindResource(key) as Brush ?? Brushes.Gray;
}
