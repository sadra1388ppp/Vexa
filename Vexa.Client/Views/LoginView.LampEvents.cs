using System.Windows;
using System.Windows.Input;

namespace NovaChat.Client.Views
{
    public partial class LoginView
    {
        private void LampCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_draggingCord)
                return;

            LampPull_MouseLeftButtonUp(LampPull, e);
        }
    }
}
