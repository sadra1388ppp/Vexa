using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _voiceLayoutFixRegistered;
    private bool _voiceLayoutAttachRegistered;

    internal static void RegisterVoiceLayoutFix()
    {
        if (_voiceLayoutFixRegistered) return;
        _voiceLayoutFixRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(VoiceLayoutFixLoaded));
    }

    private static void VoiceLayoutFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        view.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(view.NormalizeVoiceComposer));
    }

    private void NormalizeVoiceComposer()
    {
        var messageBox = FindDescendant<TextBox>(this, b => b.Name == "MessageTextBox");
        if (messageBox?.Parent is not Border inputBorder || inputBorder.Parent is not Grid grid)
            return;

        // The XAML already contains a voice button, while MainView.Media.cs
        // creates the functional/tagged button at runtime. Keep only the
        // functional runtime button and hide the duplicate XAML button.
        var voiceButtons = grid.Children
            .OfType<Button>()
            .Where(IsVoiceComposerButton)
            .ToList();

        var functionalVoice = voiceButtons.FirstOrDefault(IsFunctionalVoiceButton);

        foreach (var button in voiceButtons)
        {
            if (ReferenceEquals(button, functionalVoice)) continue;
            button.Visibility = Visibility.Collapsed;
            button.IsHitTestVisible = false;
        }

        if (functionalVoice != null)
        {
            functionalVoice.Visibility = Visibility.Visible;
            functionalVoice.IsHitTestVisible = true;
            Grid.SetColumn(functionalVoice, 1);
            functionalVoice.Width = 46;
            functionalVoice.Height = 46;
            functionalVoice.Margin = new Thickness(0);
            functionalVoice.HorizontalAlignment = HorizontalAlignment.Left;
            functionalVoice.Tag = "NovaChat.Voice";
        }

        // MediaMainViewLoaded currently adds one extra column. Remove it so
        // the composer stays exactly: attach | voice | input | send.
        while (grid.ColumnDefinitions.Count > 4)
            grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);

        Grid.SetColumn(inputBorder, 2);

        var attach = grid.Children.OfType<Button>()
            .FirstOrDefault(b => IsAttachButton(b));
        if (attach != null)
        {
            Grid.SetColumn(attach, 0);
            attach.HorizontalAlignment = HorizontalAlignment.Left;
            attach.Width = 46;
            attach.Height = 46;
            if (!_voiceLayoutAttachRegistered)
            {
                attach.Click += AttachButton_Click;
                _voiceLayoutAttachRegistered = true;
            }
        }

        var send = grid.Children.OfType<Button>()
            .FirstOrDefault(b => IsSendButton(b));
        if (send != null)
        {
            Grid.SetColumn(send, 3);
            send.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static bool IsVoiceComposerButton(Button button)
    {
        return string.Equals(button.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal)
            || string.Equals(button.ToolTip?.ToString(), "Voice message", StringComparison.Ordinal)
            || string.Equals(button.Content?.ToString(), "🎤", StringComparison.Ordinal)
            || string.Equals(button.Content?.ToString(), "🎙", StringComparison.Ordinal);
    }

    private static bool IsFunctionalVoiceButton(Button button) =>
        string.Equals(button.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal);

    private static bool IsAttachButton(Button button) =>
        string.Equals(button.ToolTip?.ToString(), "Attach file", StringComparison.Ordinal)
        || string.Equals(button.ToolTip?.ToString(), "Attach", StringComparison.Ordinal)
        || string.Equals(button.Content?.ToString(), "＋", StringComparison.Ordinal);

    private static bool IsSendButton(Button button) =>
        string.Equals(button.ToolTip?.ToString(), "Send message", StringComparison.Ordinal)
        || (button.Content?.ToString()?.Contains("➤", StringComparison.Ordinal) ?? false);
}
