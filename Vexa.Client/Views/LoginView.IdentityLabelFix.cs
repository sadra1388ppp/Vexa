using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views
{
    public partial class LoginView
    {
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // The login credential is the public Username (or Phone Number),
            // never the internal database User Id.
            Dispatcher.BeginInvoke(new Action(NormalizeLoginIdentityLabel),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void NormalizeLoginIdentityLabel()
        {
            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(LoginCard))
            {
                if (string.Equals(textBlock.Text?.Trim(), "User ID", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "Username or Phone Number";
                    return;
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            if (parent == null)
                yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                    yield return typedChild;

                foreach (T descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}
