using System.Windows;
using NAudio.Wave;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async Task ForceStopAndSendVoiceAsync()
    {
        var recorder = _voiceRecorder;
        if (recorder == null) return;

        try
        {
            recorder.StopRecording();
        }
        catch (Exception ex)
        {
            CleanupVoiceResources(deleteFile: true);
            await Dispatcher.InvokeAsync(() => MessageBox.Show(
                $"Could not stop voice recording.\n\n{ex.Message}",
                "Voice Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error));
            return;
        }

        // NAudio raises RecordingStopped asynchronously. Give its final
        // DataAvailable/flush work a moment to complete. If its handler has
        // already finalized the recording, _voiceRecorder will be null and
        // we do nothing. Otherwise finalize and upload explicitly.
        await Task.Delay(400);

        if (ReferenceEquals(_voiceRecorder, recorder))
            await FinishVoiceRecordingAsync();
    }
}
