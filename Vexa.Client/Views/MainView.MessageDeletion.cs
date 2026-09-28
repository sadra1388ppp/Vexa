using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static void OnMessageBubbleRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border clickedBorder) return;
        var mainView = FindAncestor<MainView>(clickedBorder);
        if (mainView == null || mainView._currentChatId == null) return;
        var messageBorder = FindMessageRootBorder(clickedBorder, mainView.MessagesPanel);
        if (messageBorder == null) return;
        var messageId = messageBorder.Tag is int id ? id : (int?)null;
        if (!messageId.HasValue) return;

        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy message" };
        copyItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
        copyItem.Click += mainView.CopyMessageMenuItem_Click;
        menu.Items.Add(copyItem);

        var forwardItem = new MenuItem { Header = "Forward message" };
        forwardItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
        forwardItem.Click += mainView.ForwardMessageMenuItem_Click;
        menu.Items.Add(forwardItem);

        if (messageBorder.HorizontalAlignment == HorizontalAlignment.Right)
        {
            var editItem = new MenuItem { Header = "Edit message" };
            editItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
            editItem.Click += mainView.EditMessageMenuItem_Click;
            menu.Items.Add(editItem);
        }

        var separator = new Separator();
        menu.Items.Add(separator);
        var deleteItem = new MenuItem { Header = "Delete message" };
        deleteItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
        deleteItem.Click += mainView.DeleteMessageMenuItem_Click;
        menu.Items.Add(deleteItem);

        messageBorder.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static Border? FindMessageRootBorder(DependencyObject element, DependencyObject messagePanel)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Border border && (ReferenceEquals(border.Parent, messagePanel) || messagePanel is Panel panel && panel.Children.Contains(border))) return border;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async void CopyMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info) return;

        try
        {
            var message = await GetMessageByIdAsync(_currentChatId ?? 0, info.MessageId);
            if (message == null || string.IsNullOrWhiteSpace(message.Content)) return;

            // Messages are stored as ciphertext on the server. Never copy the raw E2EE
            // envelope to the clipboard; decrypt the selected message first and copy only
            // the user-visible plaintext.
            message = await _e2ee.DecryptMessageAsync(message);
            var text = message.Content?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Media bubbles should copy the human-readable filename rather than the
            // encrypted envelope or the internal zero-width message id marker.
            if (message.MessageType is "image" or "file" or "voice")
            {
                if (!string.IsNullOrWhiteSpace(message.FileName))
                    text = message.FileName.Trim();
                else
                    text = text.Replace("📷 ", string.Empty, StringComparison.Ordinal)
                        .Replace("📎 ", string.Empty, StringComparison.Ordinal)
                        .Replace("🎙 ", string.Empty, StringComparison.Ordinal);
                var marker = text.IndexOf('\u200B');
                if (marker >= 0) text = text[..marker].Trim();
            }

            Clipboard.SetText(text);
            System.Media.SystemSounds.Asterisk.Play();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy the message.\n\n{ex.Message}", "Copy Message", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || _currentChatId == null)
            return;

        try
        {
            var message = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId);
            if (message == null)
            {
                MessageBox.Show("The message could not be located.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You can only edit your own messages.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(message.MessageType, "text", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Only text messages can be edited.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var decrypted = await _e2ee.DecryptMessageAsync(message);
            if (string.IsNullOrWhiteSpace(decrypted.Content) ||
                decrypted.Content.StartsWith("[Encrypted message", StringComparison.Ordinal))
            {
                MessageBox.Show("This message could not be decrypted on this device.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? editedText;

            // A forwarded message is a structured message: the forwarded source must
            // remain unchanged, while only the optional comment written by the sender
            // is editable. This prevents editing from flattening the forwarded card
            // into a normal text message.
            if (TryParseForwardedMessage(decrypted.Content, out var forwarded))
            {
                var editedComment = ShowEditForwardCommentDialog(forwarded.Comment);
                if (editedComment == null)
                    return;

                var forwardedSender = string.IsNullOrWhiteSpace(forwarded.Sender)
                    ? "Unknown user"
                    : forwarded.Sender.Trim();

                var forwardedPayload = $"↗ {forwardedSender}\n\n{forwarded.Message.Trim()}";

                editedText = string.IsNullOrWhiteSpace(editedComment)
                    ? forwardedPayload
                    : $"{forwardedPayload}\n\n\u200C{editedComment.Trim()}";
            }
            else
            {
                editedText = ShowEditMessageDialog(decrypted.Content);
                if (editedText == null)
                    return;
            }

            if (string.Equals(editedText, decrypted.Content, StringComparison.Ordinal))
                return;

            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                MessageBox.Show("NovaChat is not connected to the server.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var encrypted = await _e2ee.EncryptForChatAsync(message.ChatId, editedText, _apiService);
            await _hubConnection.InvokeAsync("EditMessage", message.Id, encrypted);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not edit the message.\n\n{ex.Message}", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? ShowEditMessageDialog(string currentText)
    {
        var dialog = new Window
        {
            Title = "Edit Message",
            Width = 520,
            Height = 250,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Edit your message",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBrush")
        });

        var editor = new TextBox
        {
            Text = currentText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 14, 0, 14),
            Padding = new Thickness(10),
            MinHeight = 80,
            Background = (Brush)FindResource("InputBackgroundBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButtonStyle")
        };

        var save = new Button
        {
            Content = "Save",
            Width = 90,
            Height = 36,
            Style = (Style)FindResource("PrimaryButtonStyle")
        };

        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(editor.Text))
            {
                MessageBox.Show("Message cannot be empty.", "Edit Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dialog.DialogResult = true;
        };

        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };

        return dialog.ShowDialog() == true ? editor.Text.Trim() : null;
    }

    private string? ShowEditForwardCommentDialog(string currentComment)
    {
        var dialog = new Window
        {
            Title = "Edit Forward Comment",
            Width = 520,
            Height = 250,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Edit your forwarding comment",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBrush")
        });

        var editor = new TextBox
        {
            Text = currentComment,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 14, 0, 14),
            Padding = new Thickness(10),
            MinHeight = 80,
            Background = (Brush)FindResource("InputBackgroundBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButtonStyle")
        };

        var save = new Button
        {
            Content = "Save",
            Width = 90,
            Height = 36,
            Style = (Style)FindResource("PrimaryButtonStyle")
        };

        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) => dialog.DialogResult = true;

        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };

        return dialog.ShowDialog() == true ? editor.Text.Trim() : null;
    }

    private async void DeleteMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || _currentChatId == null) return;
        var history = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId);
        if (history == null) { MessageBox.Show("The message could not be located.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var isMine = string.Equals(history.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase);
        var mode = "me";
        if (isMine)
        {
            var result = MessageBox.Show("Delete this message for everyone?\n\nChoose No to delete it only for yourself.", "Delete Message", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            mode = result == MessageBoxResult.Yes ? "everyone" : "me";
        }
        else if (MessageBox.Show("Delete this message for yourself?", "Delete Message", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            var deleted = await DeleteMessageAsync(history.Id, mode);
            if (!deleted) { MessageBox.Show("The message could not be deleted.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            await Dispatcher.InvokeAsync(() => RemoveMessageBubbleCompletely(info.Border, MessagesPanel));
            _loadedMessageIds.Remove(history.Id);
            await LoadChatsAsync();
        }
        catch (Exception ex) { MessageBox.Show($"Could not delete message.\n\n{ex.Message}", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static void RemoveMessageBubbleCompletely(Border clickedOrRoot, Panel messagesPanel)
    {
        var root = FindMessageRootBorder(clickedOrRoot, messagesPanel) ?? clickedOrRoot;
        if (messagesPanel.Children.Contains(root)) { messagesPanel.Children.Remove(root); return; }
        root.Visibility = Visibility.Collapsed; root.Height = 0; root.MinHeight = 0; root.MaxHeight = 0; root.Margin = new Thickness(0); root.Padding = new Thickness(0); root.Child = null;
    }

    private async Task<MessageModel?> GetMessageByIdAsync(int chatId, int messageId)
    {
        int? beforeId = null;
        for (var page = 0; page < 20; page++)
        {
            var endpoint = $"api/Chat/{chatId}/messages?pageSize=100";
            if (beforeId.HasValue) endpoint += $"&beforeMessageId={beforeId.Value}";
            var response = await _apiService.GetAsync<ChatHistoryResponse>(endpoint);
            if (response == null) return null;
            var match = response.Messages.FirstOrDefault(m => m.Id == messageId);
            if (match != null) return match;
            if (!response.HasMore || !response.NextBeforeMessageId.HasValue) break;
            beforeId = response.NextBeforeMessageId.Value;
        }
        return null;
    }

    private static async Task<bool> DeleteMessageAsync(int messageId, string mode)
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
        if (!string.IsNullOrWhiteSpace(AuthState.Token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/message-deletion/{messageId}") { Content = JsonContent.Create(new { Mode = mode }) };
        using var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current != null) { if (current is T match) return match; current = VisualTreeHelper.GetParent(current); }
        return null;
    }

    private sealed record MessageBubbleInfo(Border Border, int MessageId);
}
