using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class ManageUsersView
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Dispatcher.BeginInvoke(new Action(NormalizeIdentityLabels),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void NormalizeIdentityLabels()
    {
        foreach (var textBlock in FindVisualChildren<TextBlock>(this))
        {
            var text = textBlock.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.IndexOf("User ID", StringComparison.OrdinalIgnoreCase) >= 0)
                textBlock.Text = text.Replace("User ID", "Username", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var textBox in FindVisualChildren<TextBox>(this))
        {
            if (string.Equals(textBox.ToolTip?.ToString(), "Search by name, email or user ID", StringComparison.OrdinalIgnoreCase))
                textBox.ToolTip = "Search by name, username or email";
        }
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent == null)
            yield break;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
