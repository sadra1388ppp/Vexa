using NAudio.CoreAudioApi;
using NAudio.Wave;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IOPath = System.IO.Path;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly bool _voiceFixRegistered = RegisterVoiceFix();
    private Button? _voiceFixButton;
    private IWaveIn? _voiceFixRecorder;
    private WaveFileWriter? _voiceFixWriter;
    private string? _voiceFixPath;
    private DateTime _voiceFixStartedAt;
    private bool _voiceFixFinishing;

    private static bool RegisterVoiceFix()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(VoiceFixLoaded));
        return true;
    }

    private static void VoiceFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        view.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(view.InstallVoiceFix));
    }

    private void InstallVoiceFix()
    {
        var button = FindDescendant<Button>(this, b => b.Tag is string tag && tag == "NovaChat.Voice");
        if (button == null || ReferenceEquals(_voiceFixButton, button)) return;

        if (_voiceFixButton != null)
            _voiceFixButton.Click -= VoiceFixButton_Click;

        button.Click -= VoiceButton_Click;
        button.Click += VoiceFixButton_Click;
        _voiceFixButton = button;
    }

    private async void VoiceFixButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated)
            return;

        if (_voiceFixRecorder != null)
        {
            StopVoiceFixRecording();
            return;
        }

        try
        {
            IWaveIn recorder;
            WaveFormat outputFormat;

            if (WaveInEvent.DeviceCount > 0)
            {
                var waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(44100, 16, 1)
                };
                recorder = waveIn;
                outputFormat = waveIn.WaveFormat;
            }
            else
            {
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                var wasapi = new WasapiCapture(device);
                recorder = wasapi;
                outputFormat = wasapi.WaveFormat;
            }

            var tempPath = IOPath.Combine(IOPath.GetTempPath(), $"NovaChatVoice_{Guid.NewGuid():N}.wav");
            _voiceFixPath = tempPath;
            _voiceFixStartedAt = DateTime.UtcNow;
            _voiceFixWriter = new WaveFileWriter(tempPath, outputFormat);
            _voiceFixRecorder = recorder;

            recorder.DataAvailable += (_, args) =>
            {
                try
                {
                    _voiceFixWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                    _voiceFixWriter?.Flush();
                }
                catch { }
            };
            recorder.RecordingStopped += (_, _) => _ = FinishVoiceFixRecordingAsync();

            recorder.StartRecording();
            UpdateVoiceFixButton(true);
        }
        catch (Exception ex)
        {
            CleanupVoiceFixResources(deleteFile: true);
            MessageBox.Show(
                $"Could not start voice recording.\n\nCheck that Windows allows NovaChat to use your microphone.\n\nDetails: {ex.Message}",
                "Voice Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopVoiceFixRecording()
    {
        try
        {
            _voiceFixRecorder?.StopRecording();
        }
        catch (Exception ex)
        {
            CleanupVoiceFixResources(deleteFile: true);
            MessageBox.Show($"Could not stop the recording.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task FinishVoiceFixRecordingAsync()
    {
        if (_voiceFixFinishing) return;
        _voiceFixFinishing = true;

        try
        {
            var path = _voiceFixPath;
            _voiceFixPath = null;

            try { _voiceFixWriter?.Flush(); } catch { }
            try { _voiceFixWriter?.Dispose(); } catch { }
            _voiceFixWriter = null;

            var recorder = _voiceFixRecorder;
            _voiceFixRecorder = null;
            try { recorder?.Dispose(); } catch { }

            await Dispatcher.InvokeAsync(() => UpdateVoiceFixButton(false));

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            await Task.Delay(100);

            var info = new FileInfo(path);
            if (info.Length < 256)
            {
                try { File.Delete(path); } catch { }
                await Dispatcher.InvokeAsync(() => MessageBox.Show(
                    "The recording is empty. Please select a working microphone in Windows and try again.",
                    "Voice Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
                return;
            }

            var seconds = Math.Max(0.1, (DateTime.UtcNow - _voiceFixStartedAt).TotalSeconds);
            var endpoint = $"api/ChatMedia/{_currentChatId!.Value}?type=voice&durationSeconds={seconds:F2}";
            var result = await _apiService.UploadFileAsync<VoiceUploadResponse>(endpoint, path);

            if (result?.Data == null)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(
                    result?.Message ?? "Voice upload failed.",
                    "Voice Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
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
            await Dispatcher.InvokeAsync(() => MessageBox.Show(
                $"Voice message could not be sent.\n\n{ex.Message}",
                "Voice Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(_voiceFixPath))
            {
                try { File.Delete(_voiceFixPath); } catch { }
                _voiceFixPath = null;
            }
            _voiceFixFinishing = false;
        }
    }

    private void CleanupVoiceFixResources(bool deleteFile)
    {
        try { _voiceFixRecorder?.Dispose(); } catch { }
        _voiceFixRecorder = null;
        try { _voiceFixWriter?.Dispose(); } catch { }
        _voiceFixWriter = null;

        if (deleteFile && !string.IsNullOrWhiteSpace(_voiceFixPath))
        {
            try { File.Delete(_voiceFixPath); } catch { }
            _voiceFixPath = null;
        }
    }

    private void UpdateVoiceFixButton(bool recording)
    {
        if (_voiceFixButton == null) return;
        _voiceFixButton.Content = recording ? "⏹" : "🎙";
        _voiceFixButton.ToolTip = recording ? "Stop and send voice message" : "Voice message";
        _voiceFixButton.Foreground = recording
            ? Brushes.IndianRed
            : (Brush)FindResource("PrimaryBrush");
    }

    private sealed class VoiceUploadResponse
    {
        public string Message { get; set; } = string.Empty;
        public MessageModel? Data { get; set; }
    }
}
