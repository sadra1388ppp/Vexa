using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class ContactsView : UserControl
{
    public event Action? BackToChatRequested;
    public event Action<string>? ChatRequested;

    private readonly ApiService _apiService = new();

    public ContactsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadContactsAsync();
    }

    private async Task LoadContactsAsync()
    {
        try
        {
            var contacts = await _apiService.GetAsync<List<ContactModel>>("api/Contact");
            ContactsList.ItemsSource = contacts ?? new List<ContactModel>();
            StatusText.Text = contacts == null ? "Could not load contacts." : $"{contacts.Count} contact(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsList.ItemsSource = new List<UserSearchResultModel>();
            StatusText.Text = "Type a User ID, name, or email to search.";
            return;
        }

        try
        {
            StatusText.Text = "Searching...";
            var results = await _apiService.GetAsync<List<UserSearchResultModel>>(
                $"api/User/search?q={Uri.EscapeDataString(query)}");
            SearchResultsList.ItemsSource = results ?? new List<UserSearchResultModel>();
            StatusText.Text = results == null ? "Search failed." : $"{results.Count} result(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void AddSearchResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UserSearchResultModel user })
            await AddContactAsync(user.Id);
    }

    private void ChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UserSearchResultModel user })
            ChatRequested?.Invoke(user.Id);
    }

    private void ChatContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ContactModel contact })
            ChatRequested?.Invoke(contact.UserId);
    }

    private async Task AddContactAsync(string userId)
    {
        try
        {
            var result = await _apiService.PostAsync<AddContactRequest, ContactActionResponse>(
                "api/Contact", new AddContactRequest { UserId = userId });

            StatusText.Text = result?.Message ?? "Could not add contact. It may already exist.";
            if (result != null)
                await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ContactModel contact })
            return;

        if (MessageBox.Show($"Remove {contact.DisplayName} from contacts?", "NovaChat", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var removed = await _apiService.DeleteAsync($"api/Contact/{Uri.EscapeDataString(contact.UserId)}");
        StatusText.Text = removed ? "Contact removed." : "Could not remove contact.";
        if (removed)
            await LoadContactsAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();

    private sealed class ContactActionResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}