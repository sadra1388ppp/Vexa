using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async Task RenderMediaBubbleFromMessageAsync(Border border, MessageModel message)
    {
        if (message.Id <= 0) return;

        try
        {
            // Media is intentionally not E2EE. Text messages remain protected by
            // E2EE, while images/files/voice use the normal authenticated media endpoint.
            await RenderMediaBubbleAsync(border, message.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media bubble rendering failed for message {message.Id}: {ex}");
        }
    }
}
