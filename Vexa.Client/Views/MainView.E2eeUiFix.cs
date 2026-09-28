using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.ClickEvent,
            new RoutedEventHandler(E2eeCopyMenuItem_Click));

        RegisterGroupUiHandlers();
        RegisterChatRequestHandlers();
    }

    private static void E2eeCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !string.Equals(item.Header?.ToString(), "Copy message", StringComparison.Ordinal))
            return;

        var contextMenu = item.Parent as ContextMenu;
        var placementTarget = contextMenu?.PlacementTarget as DependencyObject;
        if (placementTarget == null)
            return;

        var mainView = FindAncestor<MainView>(placementTarget);
        if (mainView == null)
            return;

        var messageBorder = FindMessageRootBorderForCopy(placementTarget, mainView.MessagesPanel);
        if (messageBorder == null)
            return;

        var text = ExtractVisibleMessageText(messageBorder, mainView);
        if (string.IsNullOrWhiteSpace(text))
            return;

        e.Handled = true;
        try
        {
            Clipboard.SetText(text);
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy the message.\n\n{ex.Message}", "Copy Message", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string ExtractVisibleMessageText(Border messageBorder, MainView mainView)
    {
        if (messageBorder.Child is not StackPanel panel)
            return string.Empty;

        var candidates = panel.Children
            .OfType<TextBlock>()
            .Where(textBlock => !string.IsNullOrWhiteSpace(textBlock.Text))
            .ToList();

        if (candidates.Count == 0)
            return string.Empty;

        var chat = mainView._currentChatId.HasValue
            ? mainView._chats.FirstOrDefault(x => x.Chat.Id == mainView._currentChatId.Value)?.Chat
            : null;

        if (chat?.IsGroup == true && candidates.Count > 1)
            candidates.RemoveAt(0);

        return candidates.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
    }

    private static Border? FindMessageRootBorderForCopy(DependencyObject element, Panel messagesPanel)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Border border && messagesPanel.Children.Contains(border))
                return border;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
