using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class SettingsView : UserControl
    {
        public event Action? BackToChatRequested;
        public event Action? LogoutRequested;

        public SettingsView()
        {
            InitializeComponent();
        }

        private void LightModeButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "Resources/LightTheme.xaml",
                        UriKind.Relative)
                });
        }

        private void DarkModeButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "Resources/DarkTheme.xaml",
                        UriKind.Relative)
                });
        }

        private void BackToChatButton_Click(object sender, RoutedEventArgs e)
        {
            BackToChatRequested?.Invoke();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            LogoutRequested?.Invoke();
        }
    }
}