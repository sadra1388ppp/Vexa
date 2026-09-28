using NAudio.Wave;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _reliableVoiceFixRegistered;
    private WaveInEvent? _reliableVoiceCapture;
    private WaveFileWriter? _reliableVoiceWriter;
    private string? _reliableVoicePath;
    private DateTime _reliableVoiceStartedAt;
    private int _reliableVoiceFinishing;

    internal static void RegisterReliableVoiceFix()
    {
        if (_reliableVoiceFixRegistered) return;
        _reliableVoiceFixRegistered = true;
        EventManager.RegisterClassHandler(typeof(Button), Button.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ReliableVoiceButtonPreview), true);
    }

    private static async void ReliableVoiceButtonPreview(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button) return;
        if (FindReliableMainView(button) is not MainView view) return;

        if (string.Equals(button.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal))
        {
            e.Handled = true;
            await view.StartReliableVoiceAsync();
            return;
        }

        if (string.Equals(button.Content?.ToString(), "➤", StringComparison.Ordinal) && view._reliableVoiceCapture != null)
        {
            e.Handled = true;
            await view.StopAndUploadReliableVoiceAsync();
        }
    }

    private static MainView? FindReliableMainView(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is MainView view) return view;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async Task StartReliableVoiceAsync()
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated || _reliableVoiceCapture != null) return;

        if (WaveInEvent.DeviceCount <= 0)
        {
            MessageBox.Show("No microphone was detected on this computer.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NovaChatVoice_{Guid.NewGuid():N}.wav");
            var capture = new WaveInEvent
            {
                DeviceNumber = 0,
                BufferMilliseconds = 100,
                NumberOfBuffers = 3,
                WaveFormat = new WaveFormat(44100, 16, 1)
            };
            var writer = new WaveFileWriter(path, capture.WaveFormat);

            capture.DataAvailable += (_, args) =>
            {
                try { writer.Write(args.Buffer, 0, args.BytesRecorded); }
                catch { }
            };

            _reliableVoicePath = path;
            _reliableVoiceStartedAt = DateTime.UtcNow;
            _reliableVoiceCapture = capture;
            _reliableVoiceWriter = writer;
            _reliableVoiceFinishing = 0;

            capture.StartRecording();
            await Dispatcher.InvokeAsync(() => UpdateReliableVoiceButton(true));
        }
        catch (Exception ex)
        {
            CleanupReliableVoice(true);
            MessageBox.Show($"Could not start voice recording.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopAndUploadReliableVoiceAsync()
    {
        if (Interlocked.Exchange(ref _reliableVoiceFinishing, 1) != 0) return;

        var capture = _reliableVoiceCapture;
        var writer = _reliableVoiceWriter;
        var path = _reliableVoicePath;
        var startedAt = _reliableVoiceStartedAt;

        if (capture == null || writer == null || string.IsNullOrWhiteSpace(path))
        {
            Interlocked.Exchange(ref _reliableVoiceFinishing, 0);
            return;
        }

        try
        {
            try { capture.StopRecording(); } catch { }
            await Task.Delay(200);

            _reliableVoiceCapture = null;
            _reliableVoiceWriter = null;
            _reliableVoicePath = null;

            try { writer.Flush(); } catch { }
            try { writer.Dispose(); } catch { }
            try { capture.Dispose(); } catch { }

            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show("The voice recording could not be created.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var info = new System.IO.FileInfo(path);
            if (info.Length <= 1000)
            {
                try { System.IO.File.Delete(path); } catch { }
                MessageBox.Show("The recording is empty. Please check Windows microphone permissions.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var duration = Math.Max(0.1, (DateTime.UtcNow - startedAt).TotalSeconds);
            await UploadMediaAsync("voice", path, duration);
            try { System.IO.File.Delete(path); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Voice could not be sent.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => UpdateReliableVoiceButton(false));
            Interlocked.Exchange(ref _reliableVoiceFinishing, 0);
        }
    }

    private void UpdateReliableVoiceButton(bool recording)
    {
        var button = FindDescendant<Button>(this, b => string.Equals(b.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal));
        if (button == null) return;
        button.Content = recording ? "⏹" : "🎙";
        button.ToolTip = recording ? "Stop and send voice message" : "Voice message";
        button.Foreground = recording ? Brushes.IndianRed : (Brush)FindResource("PrimaryBrush");
    }

    private void CleanupReliableVoice(bool deleteFile)
    {
        try { _reliableVoiceCapture?.StopRecording(); } catch { }
        try { _reliableVoiceCapture?.Dispose(); } catch { }
        try { _reliableVoiceWriter?.Dispose(); } catch { }
        _reliableVoiceCapture = null;
        _reliableVoiceWriter = null;
        if (deleteFile && !string.IsNullOrWhiteSpace(_reliableVoicePath))
        {
            try { System.IO.File.Delete(_reliableVoicePath); } catch { }
            _reliableVoicePath = null;
        }
    }
}
