using NovaChat.Client.Views;
using System.Windows;

namespace NovaChat.Client;

public partial class MainWindow
{
    public void ShowAllChats()
    {
        if (!_isOwner)
            return;

        MainContainer.Children.Clear();
        var allChatsView = new AllChatsView();
        allChatsView.BackToChatRequested += ShowMain;
        MainContainer.Children.Add(allChatsView);
    }
}
