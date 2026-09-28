using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void ChatUserNameText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ChatUserNameText_Click(sender, e);
    }
}
