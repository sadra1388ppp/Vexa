using Microsoft.Win32;
using NAudio.Wave;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _mediaUiRegistered;
    private WaveInEvent? _voiceRecorder;
    private WaveFileWriter? _voiceWriter;
    private string? _voicePath;
    private DateTime _voiceStartedAt;
    private readonly Dictionary<int, SoundPlayer> _voicePlayers = [];

    internal static void RegisterMediaFeatures()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(MediaMainViewLoaded));
        EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.LoadedEvent, new RoutedEventHandler(MediaBubbleLoaded));
    }

    private static void MediaMainViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.AttachMediaControls();
    }

    private void AttachMediaControls()
    {
        if (_mediaUiRegistered) return;
        _mediaUiRegistered = true;

        var attach = FindDescendant<Button>(this, b => b.ToolTip is string s && s == "Attach");
        if (attach != null) attach.Click += AttachButton_Click;

        var messageBox = FindDescendant<TextBox>(this, b => b.Name == "MessageTextBox");
        var border = messageBox?.Parent as Border;
        var grid = border?.Parent as Grid;
        if (grid == null) return;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var send = grid.Children.OfType<Button>().FirstOrDefault(b => string.Equals(b.Content?.ToString(), "➤", StringComparison.Ordinal));
        if (send != null) Grid.SetColumn(send, 3);

        var voice = new Button
        {
            Content = "🎙",
            Width = 42,
            Height = 42,
            Margin = new Thickness(2, 0, 0, 0),
            Background = (Brush)FindResource("PanelBackgroundBrush"),
            Foreground = (Brush)FindResource("PrimaryBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            ToolTip = "Voice message",
            Tag = "NovaChat.Voice"
        };
        Grid.SetColumn(voice, 2);
        grid.Children.Add(voice);
        voice.Click += VoiceButton_Click;
    }

    private async void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated) return;

        var dialog = new OpenFileDialog
        {
            Title = "Send photo or file",
            Filter = "Photos and files|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.zip;*.rar;*.7z;*.txt;*.csv;*.json;*.mp4;*.mov;*.mkv;*.webm;*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        var ext = IOPath.GetExtension(dialog.FileName);
        var type = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }
            .Contains(ext, StringComparer.OrdinalIgnoreCase) ? "image" : "file";

        await UploadMediaAsync(type, dialog.FileName);
    }

    private async void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated) return;

        if (_voiceRecorder != null)
        {
            StopVoiceRecording();
            return;
        }

        try
        {
            if (WaveInEvent.DeviceCount <= 0)
            {
                MessageBox.Show("No microphone was detected on this computer.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var temp = IOPath.Combine(IOPath.GetTempPath(), $"NovaChatVoice_{Guid.NewGuid():N}.wav");
            _voicePath = temp;
            _voiceStartedAt = DateTime.UtcNow;
            _voiceWriter = new WaveFileWriter(temp, new WaveFormat(44100, 16, 1));
            _voiceRecorder = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(44100, 16, 1)
            };

            _voiceRecorder.DataAvailable += (_, args) =>
            {
                try { _voiceWriter?.Write(args.Buffer, 0, args.BytesRecorded); }
                catch { }
            };
            _voiceRecorder.RecordingStopped += (_, _) => _ = FinishVoiceRecordingAsync();
            _voiceRecorder.StartRecording();
            UpdateVoiceButtonState(true);
        }
        catch (Exception ex)
        {
            CleanupVoiceResources(deleteFile: true);
            MessageBox.Show($"Could not start voice recording.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task FinishVoiceRecordingAsync()
    {
        try
        {
            _voiceWriter?.Dispose();
            _voiceWriter = null;

            var recorder = _voiceRecorder;
            _voiceRecorder = null;
            try { recorder?.Dispose(); } catch { }

            var path = _voicePath;
            _voicePath = null;

            if (path == null || !File.Exists(path))
                return;

            var info = new FileInfo(path);
            if (info.Length < 100)
            {
                try { File.Delete(path); } catch { }
                await Dispatcher.InvokeAsync(() => MessageBox.Show("The recording was empty. Please check your microphone permissions and try again.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var seconds = Math.Max(0.1, (DateTime.UtcNow - _voiceStartedAt).TotalSeconds);
            await UploadMediaAsync("voice", path, seconds);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => UpdateVoiceButtonState(false));
            if (!string.IsNullOrWhiteSpace(_voicePath))
            {
                try { File.Delete(_voicePath); } catch { }
                _voicePath = null;
            }
        }
    }

    private void StopVoiceRecording()
    {
        try
        {
            _voiceRecorder?.StopRecording();
        }
        catch
        {
            CleanupVoiceResources(deleteFile: true);
        }
    }

    private void CleanupVoiceResources(bool deleteFile)
    {
        try { _voiceRecorder?.Dispose(); } catch { }
        _voiceRecorder = null;
        try { _voiceWriter?.Dispose(); } catch { }
        _voiceWriter = null;

        if (deleteFile && !string.IsNullOrWhiteSpace(_voicePath))
        {
            try { File.Delete(_voicePath); } catch { }
            _voicePath = null;
        }
    }

    private void UpdateVoiceButtonState(bool recording)
    {
        var button = FindDescendant<Button>(this, b => b.Tag is string s && s == "NovaChat.Voice");
        if (button == null) return;

        button.Content = recording ? "⏹" : "🎙";
        button.ToolTip = recording ? "Stop and send voice message" : "Voice message";
        button.Foreground = recording ? Brushes.IndianRed : (Brush)FindResource("PrimaryBrush");
    }

    private async Task UploadMediaAsync(string type, string path, double? durationSeconds = null)
    {
        if (!_currentChatId.HasValue) return;

        try
        {
            var endpoint = $"api/ChatMedia/{_currentChatId.Value}?type={Uri.EscapeDataString(type)}";
            if (durationSeconds.HasValue)
                endpoint += $"&durationSeconds={durationSeconds.Value.ToString("F2", CultureInfo.InvariantCulture)}";

            var result = await _apiService.UploadFileAsync<MediaUploadResponse>(endpoint, path);
            if (result?.Data == null)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(result?.Message ?? "Media upload failed.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (_loadedMessageIds.Add(result.Data.Id)) AddMessageToUi(result.Data);
                UpdateChatPreview(result.Data);
            });
            await ScrollMessagesToBottomAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Media could not be sent.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private static void MediaBubbleLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is int) return;

        var text = border.Child is StackPanel panel
            ? panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text
            : null;

        if (string.IsNullOrWhiteSpace(text)) return;

        var match = Regex.Match(text, @"\u200B(\d+)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var messageId)) return;

        border.Tag = messageId;
        if (FindAncestorMainView(border) is MainView view)
            _ = view.RenderMediaBubbleAsync(border, messageId);
    }

    private static bool IsMediaMessageBorder(Border border) =>
        border.Child is StackPanel panel &&
        panel.Children.OfType<TextBlock>().Any(t => t.Text.Contains("\u200B", StringComparison.Ordinal));

    private async Task RenderMediaBubbleAsync(Border border, int messageId)
    {
        if (!IsMediaMessageBorder(border)) return;

        var existing = border.Child as StackPanel;
        var label = existing?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;

        if (label.StartsWith("🎙", StringComparison.Ordinal))
        {
            ReplaceWithVoiceBubble(border, messageId, label);
            return;
        }

        if (label.StartsWith("📷", StringComparison.Ordinal))
        {
            await ReplaceWithImageBubbleAsync(border, messageId, label);
            return;
        }

        ReplaceWithFileBubble(border, messageId, label);
    }

    private async Task ReplaceWithImageBubbleAsync(Border border, int messageId, string label)
    {
        var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
        if (bytes == null || bytes.Length == 0) return;

        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();

        var mine = border.HorizontalAlignment == HorizontalAlignment.Right;
        var stack = NewMediaStack();
        var imageCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Clip = new RectangleGeometry(new Rect(0, 0, 320, 320), 10, 10)
        };
        var imageControl = new Image
        {
            Source = image,
            Width = 320,
            MaxHeight = 320,
            Stretch = Stretch.Uniform,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        imageControl.MouseLeftButtonUp += async (_, _) => await SaveImageCopyAsync(messageId, CleanMediaLabel(label));
        imageCard.Child = imageControl;

        stack.Children.Add(imageCard);
        stack.Children.Add(new TextBlock
        {
            Text = CleanMediaLabel(label),
            Foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush"),
            FontSize = 12,
            Margin = new Thickness(2, 7, 2, 0),
            TextWrapping = TextWrapping.Wrap
        });
        AddMediaTime(stack, border, mine);
        border.Child = stack;
    }

    private void ReplaceWithFileBubble(Border border, int messageId, string label)
    {
        var mine = border.HorizontalAlignment == HorizontalAlignment.Right;
        var foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush");
        var secondary = mine ? Brushes.White : (Brush)FindResource("SecondaryTextBrush");
        var accent = (Brush)FindResource("PrimaryBrush");

        var stack = NewMediaStack();
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = mine ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) : (Brush)FindResource("BorderBrush"),
            Background = mine ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : (Brush)FindResource("PanelBackgroundBrush"),
            Padding = new Thickness(12)
        };

        var row = new DockPanel();
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(9),
            Background = mine ? new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)) : (Brush)FindResource("WindowBackgroundBrush"),
            Child = new TextBlock
            {
                Text = GetFileIcon(CleanMediaLabel(label)),
                FontSize = 21,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var openButton = new Button
        {
            Content = "Save",
            MinWidth = 62,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        openButton.Click += async (_, _) => await DownloadMediaAsync(messageId, CleanMediaLabel(label));
        DockPanel.SetDock(openButton, Dock.Right);

        var textPanel = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = CleanMediaLabel(label).Replace("📎 ", string.Empty),
            Foreground = foreground,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = "File • Tap Save to open",
            Foreground = secondary,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            Opacity = 0.82
        });

        row.Children.Add(icon);
        row.Children.Add(textPanel);
        row.Children.Add(openButton);
        card.Child = row;
        stack.Children.Add(card);
        AddMediaTime(stack, border, mine);
        border.Child = stack;
    }

    private void ReplaceWithVoiceBubble(Border border, int messageId, string label)
    {
        var mine = border.HorizontalAlignment == HorizontalAlignment.Right;
        var foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush");
        var secondary = mine ? Brushes.White : (Brush)FindResource("SecondaryTextBrush");

        var stack = NewMediaStack();
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = mine ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : (Brush)FindResource("PanelBackgroundBrush"),
            BorderBrush = mine ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) : (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10)
        };

        var button = new Button
        {
            Content = "▶  Play voice message",
            Padding = new Thickness(12, 8, 12, 8),
            Background = Brushes.Transparent,
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += async (_, _) => await PlayVoiceAsync(messageId, button);

        var filename = CleanMediaLabel(label).Replace("🎙 ", string.Empty);
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(new TextBlock
        {
            Text = filename,
            Foreground = secondary,
            FontSize = 11,
            Margin = new Thickness(6, 1, 6, 0)
        });
        card.Child = panel;
        stack.Children.Add(card);
        AddMediaTime(stack, border, mine);
        border.Child = stack;
    }

    private static StackPanel NewMediaStack() => new() { Margin = new Thickness(0) };

    private static void AddMediaTime(StackPanel stack, Border border, bool mine)
    {
        var time = border.ContextMenu?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(time)) return;
        stack.Children.Add(new TextBlock
        {
            Text = time,
            Foreground = mine ? Brushes.White : Brushes.Gray,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 4, 2, 0),
            Opacity = 0.78
        });
    }

    private static string CleanMediaLabel(string value)
    {
        var index = value.IndexOf('\u200B');
        return index >= 0 ? value[..index].Trim() : value.Trim();
    }

    private static string GetFileIcon(string value)
    {
        var ext = IOPath.GetExtension(value).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "📕",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📗",
            ".ppt" or ".pptx" => "📙",
            ".zip" or ".rar" or ".7z" => "🗜",
            ".mp4" or ".mov" or ".mkv" or ".webm" => "🎬",
            ".txt" or ".csv" or ".json" => "📄",
            _ => "📎"
        };
    }

    private async Task DownloadMediaAsync(int messageId, string suggestedName)
    {
        var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
        if (bytes == null) return;

        var dialog = new SaveFileDialog
        {
            FileName = suggestedName.Replace("📎 ", string.Empty),
            Title = "Save file"
        };
        if (dialog.ShowDialog() == true)
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
    }

    private async Task SaveImageCopyAsync(int messageId, string suggestedName)
    {
        var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
        if (bytes == null) return;

        var dialog = new SaveFileDialog
        {
            FileName = string.IsNullOrWhiteSpace(suggestedName) ? $"image_{messageId}.jpg" : suggestedName,
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.gif|All files|*.*",
            Title = "Save image"
        };
        if (dialog.ShowDialog() == true)
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
    }

    private async Task PlayVoiceAsync(int messageId, Button button)
    {
        try
        {
            if (_voicePlayers.TryGetValue(messageId, out var existing))
            {
                existing.Stop();
                existing.Dispose();
                _voicePlayers.Remove(messageId);
                button.Content = "▶  Play voice message";
                return;
            }

            var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
            if (bytes == null || bytes.Length == 0) return;

            var path = IOPath.Combine(IOPath.GetTempPath(), $"NovaChatPlay_{messageId}.wav");
            await File.WriteAllBytesAsync(path, bytes);
            var player = new SoundPlayer(path);
            player.Load();
            player.Play();
            _voicePlayers[messageId] = player;
            button.Content = "▶  Replay voice";
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Voice could not be played.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    private static MainView? FindAncestorMainView(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is MainView view) return view;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T typed && predicate(typed)) return typed;
            var nested = FindDescendant(child, predicate);
            if (nested != null) return nested;
        }
        return null;
    }

    private sealed class MediaUploadResponse
    {
        public string Message { get; set; } = string.Empty;
        public MessageModel? Data { get; set; }
    }
}
