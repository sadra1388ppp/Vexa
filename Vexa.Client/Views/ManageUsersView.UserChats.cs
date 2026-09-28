using System.Windows;

namespace NovaChat.Client.Views;

public partial class ManageUsersView
{
    private void ViewChatsButton_Click(object sender, RoutedEventArgs e) => _ = OpenUserChatsAsync();

    private async Task OpenUserChatsAsync()
    {
        if (_selectedUser == null || _isSaving) return;

        ViewChatsButton.IsEnabled = false;
        try
        {
            var window = new OwnerUserChatsWindow(_selectedUser.Id, _selectedUser.DisplayName)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
        finally
        {
            UpdateViewChatsButtonState();
        }
    }

    private void UpdateViewChatsButtonState()
        => ViewChatsButton.IsEnabled = _selectedUser != null && !_isSaving;
}
