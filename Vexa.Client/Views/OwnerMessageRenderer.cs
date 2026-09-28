using NovaChat.Client.Services;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using IOPath = System.IO.Path;

namespace NovaChat.Client.Views;

internal static class OwnerMessageRenderer
{
    public static async Task<FrameworkElement> BuildAsync(
        FrameworkElement resourceOwner,
        ApiService api,
        int chatId,
        int messageId,
        string senderName,
        string senderId,
        string content,
        DateTime sentAt,
        string messageType,
        string? fileName,
        string? contentType,
        long? fileSize,
        double? durationSeconds,
        bool encryptedMedia,
        Func<Task<byte[]?>>? loadMediaBytesAsync,
        CancellationToken cancellationToken = default)
    {
        var outer = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = FindBrush(resourceOwner, "PanelBackgroundBrush"),
            BorderBrush = FindBrush(resourceOwner, "BorderBrush"),
            BorderThickness = new Thickness(1)
        };

        var body = new StackPanel();
        var sender = string.IsNullOrWhiteSpace(senderName) ? senderId : senderName;
        body.Children.Add(new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new TextBlock
                {
                    Text = sender,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush(resourceOwner, "TextBrush")
                },
                CreateTimeText(resourceOwner, sentAt)
            }
        });

        var normalizedType = (messageType ?? "text").Trim().ToLowerInvariant();
        if (normalizedType is "image" or "voice" or "file" or "e2ee-media")
        {
            if (loadMediaBytesAsync != null && (normalizedType != "e2ee-media" || encryptedMedia))
            {
                try
                {
                    var bytes = await loadMediaBytesAsync();
                    if (bytes != null && bytes.Length > 0)
                    {
                        if (normalizedType == "e2ee-media") normalizedType = GuessMediaType(contentType, fileName);
                        var mediaControl = BuildMediaControl(resourceOwner, normalizedType, fileName, contentType, fileSize, durationSeconds, bytes);
                        if (mediaControl != null)
                        {
                            body.Children.Add(mediaControl);
                            outer.Child = body;
                            return outer;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Owner media rendering failed for message {messageId}: {ex}");
                }
            }

            body.Children.Add(BuildFallbackMediaCard(resourceOwner, normalizedType, fileName, fileSize, durationSeconds));
        }
        else
        {
            body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(content) ? "[No text content]" : content,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 13,
                Foreground = FindBrush(resourceOwner, "TextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        outer.Child = body;
        return outer;
    }

    private static TextBlock CreateTimeText(FrameworkElement owner, DateTime sentAt)
    {
        var text = new TextBlock
        {
            Text = sentAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm:ss", CultureInfo.InvariantCulture),
            FontSize = 10,
            Foreground = FindBrush(owner, "SecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0)
        };
        DockPanel.SetDock(text, Dock.Right);
        return text;
    }

    private static FrameworkElement? BuildMediaControl(
        FrameworkElement owner,
        string type,
        string? fileName,
        string? contentType,
        long? fileSize,
        double? durationSeconds,
        byte[] bytes)
    {
        return type switch
        {
            "image" => BuildImage(owner, fileName, fileSize, bytes),
            "voice" => BuildVoice(owner, fileName, fileSize, durationSeconds, bytes),
            "file" => BuildFileCard(owner, fileName, contentType, fileSize),
            _ => BuildFileCard(owner, fileName, contentType, fileSize)
        };
    }

    private static FrameworkElement BuildImage(FrameworkElement owner, string? fileName, long? fileSize, byte[] bytes)
    {
        var image = new System.Windows.Controls.Image
        {
            Source = CreateBitmap(bytes),
            Width = 260,
            MaxHeight = 260,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(image);
        panel.Children.Add(new TextBlock
        {
            Text = BuildMediaMeta(fileName, fileSize, null),
            FontSize = 10,
            Foreground = FindBrush(owner, "SecondaryTextBrush"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        return panel;
    }

    private static FrameworkElement BuildVoice(FrameworkElement owner, string? fileName, long? fileSize, double? durationSeconds, byte[] bytes)
    {
        var path = IOPath.Combine(IOPath.GetTempPath(), "NovaChat", $"owner-voice-{Guid.NewGuid():N}.wav");
        Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);

        var player = new SoundPlayer(path);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        var play = CreateActionButton(owner, "▶  Play");
        var stop = CreateActionButton(owner, "■  Stop");
        play.Click += (_, _) =>
        {
            try { player.Play(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Owner voice playback failed: {ex}"); }
        };
        stop.Click += (_, _) =>
        {
            try { player.Stop(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Owner voice stop failed: {ex}"); }
        };
        row.Children.Add(play);
        row.Children.Add(stop);

        var panel = new StackPanel();
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = BuildMediaMeta(fileName, fileSize, durationSeconds),
            FontSize = 10,
            Foreground = FindBrush(owner, "SecondaryTextBrush"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        return panel;
    }

    private static Button CreateActionButton(FrameworkElement owner, string content)
    {
        return new Button
        {
            Content = content,
            Width = 92,
            Height = 32,
            Margin = new Thickness(0, 0, 7, 0),
            Style = FindStyle(owner, "SecondaryButtonStyle")
        };
    }

    private static FrameworkElement BuildFileCard(FrameworkElement owner, string? fileName, string? contentType, long? fileSize)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            Background = FindBrush(owner, "InputBackgroundBrush")
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"📎  {fileName ?? "Attachment"}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush(owner, "TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = BuildMediaMeta(null, fileSize, null) + (string.IsNullOrWhiteSpace(contentType) ? string.Empty : $"  •  {contentType}"),
            FontSize = 10,
            Foreground = FindBrush(owner, "SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        card.Child = panel;
        return card;
    }

    private static FrameworkElement BuildFallbackMediaCard(FrameworkElement owner, string type, string? fileName, long? fileSize, double? durationSeconds)
    {
        var icon = type switch
        {
            "image" => "📷",
            "voice" => "🎙",
            _ => "📎"
        };
        var card = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            Background = FindBrush(owner, "InputBackgroundBrush")
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{icon}  {fileName ?? "Media attachment"}",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush(owner, "TextBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = BuildMediaMeta(null, fileSize, durationSeconds),
            FontSize = 10,
            Foreground = FindBrush(owner, "SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        card.Child = panel;
        return card;
    }

    private static string GuessMediaType(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "image";
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "voice";
        }
        var extension = IOPath.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" ? "image" :
            extension == ".wav" ? "voice" : "file";
    }

    private static string BuildMediaMeta(string? fileName, long? size, double? duration)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fileName)) parts.Add(fileName);
        if (size.HasValue && size.Value >= 0) parts.Add(FormatBytes(size.Value));
        if (duration.HasValue && duration.Value >= 0) parts.Add(TimeSpan.FromSeconds(duration.Value).ToString(duration.Value >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "Media attachment" : string.Join("  •  ", parts);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private static BitmapImage CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Brush FindBrush(FrameworkElement owner, string key) =>
        owner.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Style? FindStyle(FrameworkElement owner, string key) =>
        owner.TryFindResource(key) as Style;
}
