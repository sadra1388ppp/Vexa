namespace NovaChat.Client.Views;

public partial class MainView
{
    // Legacy voice handler intentionally disabled.
    // MainView.VoiceReliableFix.cs is the sole owner of voice recording/sending.
    internal static void RegisterVoiceSendFix()
    {
    }
}
