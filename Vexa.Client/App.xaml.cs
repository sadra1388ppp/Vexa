using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NovaChat.Client.Services;
using NovaChat.Client.Views;

namespace NovaChat.Client;

public partial class App : Application
{
    private static readonly object LogLock = new();
    private static string LogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaChat", "logs", "client-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        Exit += OnApplicationExit;

        try
        {
            MainView.RegisterMediaFeatures();
            MainView.RegisterVoiceLayoutFix();
            MainView.RegisterLiveRefresh();
            MainView.RegisterVoiceSendFix();
            MainView.RegisterImageViewer();
        }
        catch (Exception ex)
        {
            LogException("Startup feature registration", ex);
            MessageBox.Show("NovaChat could not initialize all client features. The error has been logged.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }

    private void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        NotificationService.Dispose();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        MessageBox.Show("An unexpected error occurred. NovaChat will keep running.\n\nThe error was written to the client crash log.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("AppDomain.UnhandledException", ex);
        else
            WriteLog($"AppDomain.UnhandledException\n{e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string source, Exception ex) =>
        WriteLog($"{DateTime.Now:O}\nSOURCE: {source}\n{ex}\n--------------------------------------------------");

    private static void WriteLog(string text)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
        }
        catch
        {
            Debug.WriteLine(text);
        }
    }
}
