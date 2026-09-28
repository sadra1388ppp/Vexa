using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _imageViewerRegistered;

    internal static void RegisterImageViewer()
    {
        if (_imageViewerRegistered) return;
        _imageViewerRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Image),
            Image.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ImagePreviewMouseUp),
            true);
    }

    private static async void ImagePreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image) return;
        if (FindImageViewerMainView(image) is not MainView view) return;
        if (FindMessageId(image) is not int messageId || messageId <= 0) return;

        e.Handled = true;
        await view.ShowImageViewerAsync(messageId);
    }

    private static MainView? FindImageViewerMainView(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is MainView view) return view;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static int? FindMessageId(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is Border border && border.Tag is int id) return id;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async Task ShowImageViewerAsync(int messageId)
    {
        try
        {
            var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
            if (bytes == null || bytes.Length == 0)
            {
                MessageBox.Show("The image could not be loaded.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            var viewer = new Window
            {
                Title = "NovaChat • Image",
                Width = 980,
                Height = 760,
                MinWidth = 520,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = Brushes.Black,
                ShowInTaskbar = false
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbar = new DockPanel
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 18, 18, 22)),
                LastChildFill = false
            };

            var title = new TextBlock
            {
                Text = "Image",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };
            DockPanel.SetDock(title, Dock.Left);

            var save = new Button
            {
                Content = "Save image",
                Width = 112,
                Height = 34,
                Margin = new Thickness(0, 0, 12, 0),
                Background = (Brush)FindResource("PrimaryBrush"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            save.Click += async (_, _) => await SaveImageCopyAsync(messageId, $"image_{messageId}.jpg");
            DockPanel.SetDock(save, Dock.Right);

            var close = new Button
            {
                Content = "✕",
                Width = 38,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            close.Click += (_, _) => viewer.Close();
            DockPanel.SetDock(close, Dock.Right);

            toolbar.Children.Add(title);
            toolbar.Children.Add(close);
            toolbar.Children.Add(save);

            var imageHost = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Black
            };
            imageHost.Content = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = 900,
                MaxHeight = 650,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(toolbar, 0);
            Grid.SetRow(imageHost, 1);
            root.Children.Add(toolbar);
            root.Children.Add(imageHost);
            viewer.Content = root;
            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the image.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
