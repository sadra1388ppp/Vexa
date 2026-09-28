using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly HashSet<int> _requestedMessageKeyIds = [];

    private async Task<MessageModel> DecryptMessageWithRecoveryAsync(MessageModel message)
    {
        var rawContent = message.Content;
        var decrypted = await _e2ee.DecryptMessageAsync(message);

        if (string.Equals(
                decrypted.Content,
                "[Encrypted message — this device has no key]",
                StringComparison.Ordinal))
        {
            if (_requestedMessageKeyIds.Add(message.Id))
            {
                try
                {
                    if (_hubConnection?.State == HubConnectionState.Connected &&
                        !string.IsNullOrWhiteSpace(_e2ee.DeviceId))
                    {
                        await _hubConnection.InvokeAsync(
                            "RequestMessageKey",
                            message.Id,
                            _e2ee.DeviceId);
                    }
                    else
                    {
                        _requestedMessageKeyIds.Remove(message.Id);
                    }
                }
                catch (Exception ex)
                {
                    _requestedMessageKeyIds.Remove(message.Id);
                    System.Diagnostics.Debug.WriteLine(
                        $"E2EE key recovery request failed for message {message.Id}: {ex}");
                }
            }
        }

        return decrypted;
    }

    private async Task OnMessageKeyRequested(MessageKeyRequestedPayload payload)
    {
        if (payload == null ||
            payload.MessageId <= 0 ||
            payload.ChatId <= 0 ||
            string.IsNullOrWhiteSpace(payload.RequesterDeviceId) ||
            string.IsNullOrWhiteSpace(payload.RequesterPublicKeyPem))
            return;

        try
        {
            var message = await GetMessageByIdAsync(payload.ChatId, payload.MessageId);
            if (message == null || message.IsDeletedForEveryone)
                return;

            if (!_e2ee.TryRewrapMessageKeyForDevice(
                    message.Content,
                    payload.RequesterPublicKeyPem,
                    out var wrappedKey))
                return;

            if (_hubConnection?.State != HubConnectionState.Connected)
                return;

            await _hubConnection.InvokeAsync(
                "DeliverMessageKey",
                payload.MessageId,
                payload.ChatId,
                payload.RequesterUserId,
                payload.RequesterDeviceId,
                wrappedKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"E2EE key recovery response failed for message {payload.MessageId}: {ex}");
        }
    }

    private async Task OnMessageKeyDelivered(MessageKeyDeliveredPayload payload)
    {
        if (payload == null ||
            payload.MessageId <= 0 ||
            payload.ChatId <= 0 ||
            !string.Equals(payload.DeviceId, _e2ee.DeviceId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload.WrappedKey))
            return;

        try
        {
            var message = await GetMessageByIdAsync(payload.ChatId, payload.MessageId);
            if (message == null)
                return;

            if (!_e2ee.AcceptRecoveredMessageKey(
                    message.Id,
                    message.Content,
                    payload.WrappedKey))
                return;

            _requestedMessageKeyIds.Remove(payload.MessageId);

            if (_currentChatId == payload.ChatId)
            {
                await Dispatcher.InvokeAsync(
                    async () => await ReloadCurrentChatMessagesAfterKeyRecoveryAsync(payload.ChatId),
                    DispatcherPriority.Normal);
            }
            else
            {
                try
                {
                    var refreshed = await GetMessageByIdAsync(payload.ChatId, payload.MessageId);
                    if (refreshed != null)
                    {
                        refreshed = await _e2ee.DecryptMessageAsync(refreshed);
                        await Dispatcher.InvokeAsync(
                            () => UpdateChatPreview(refreshed),
                            DispatcherPriority.Normal);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Recovered message preview refresh failed for {payload.MessageId}: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"E2EE recovered key handling failed for message {payload.MessageId}: {ex}");
        }
    }

    private async Task ReloadCurrentChatMessagesAfterKeyRecoveryAsync(int chatId)
    {
        if (_currentChatId != chatId)
            return;

        MessagesPanel.Children.Clear();
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;
        UpdateLoadOlderButton();

        await LoadInitialMessagesAsync(chatId);
        await ScrollMessagesToBottomAsync();
    }

    private sealed class MessageKeyRequestedPayload
    {
        public int MessageId { get; set; }
        public int ChatId { get; set; }
        public string RequesterUserId { get; set; } = string.Empty;
        public string RequesterDeviceId { get; set; } = string.Empty;
        public string RequesterPublicKeyPem { get; set; } = string.Empty;
        public string RequesterUsername { get; set; } = string.Empty;
    }

    private sealed class MessageKeyDeliveredPayload
    {
        public int MessageId { get; set; }
        public int ChatId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string WrappedKey { get; set; } = string.Empty;
    }
}
