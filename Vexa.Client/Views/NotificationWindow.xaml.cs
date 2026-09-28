using System.Windows;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public NotificationWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _closeTimer.Tick += CloseTimer_Tick;
        Loaded += NotificationWindow_Loaded;
        Closed += NotificationWindow_Closed;
    }

    private void NotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _closeTimer.Start();
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        Close();
    }

    private void NotificationWindow_Closed(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        _closeTimer.Tick -= CloseTimer_Tick;
        Loaded -= NotificationWindow_Loaded;
        Closed -= NotificationWindow_Closed;
    }
}
