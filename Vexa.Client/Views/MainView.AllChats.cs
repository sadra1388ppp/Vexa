using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void AllChatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOwner)
            return;

        if (Window.GetWindow(this) is NovaChat.Client.MainWindow mainWindow)
            mainWindow.ShowAllChats();
    }
}
