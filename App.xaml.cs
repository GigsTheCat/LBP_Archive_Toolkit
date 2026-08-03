using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LbpArchiveToolkit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Global\LbpArchiveToolkit_SingleInstanceMutex";
        
        _instanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("Another instance of LBP Archive Toolkit is already running.", "App Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
        
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit");
            Directory.CreateDirectory(appDataFolder);

            string crashLogPath = Path.Combine(appDataFolder, "crash_log.txt");
            string errorMsg = $"[{DateTime.Now}] Unhandled Exception:\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}\n\n";

            File.AppendAllText(crashLogPath, errorMsg);

            var result = MessageBox.Show($"A fatal error occurred and the application must close.\n\nError: {e.Exception.Message}\n\nA crash log has been saved to:\n{crashLogPath}\n\nWould you like to open the folder containing the crash log now?",
                            "Fatal Error", MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{crashLogPath}\"");
            }
        }
        catch
        {
            // If the IO/Logging subsystem fails, exit safely to prevent infinite loops
        }

        e.Handled = true;
        Current.Shutdown(1);
    }
}

public static class LogManager
{
    private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit");
    private static readonly string LogPath = Path.Combine(AppDataFolder, "debug_log.txt");
    private static readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>();

    static LogManager()
    {
        Task.Run(ProcessLogQueueAsync);
    }

    private static async Task ProcessLogQueueAsync()
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            
            using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };

            await foreach (var message in _logChannel.Reader.ReadAllAsync())
            {
                try
                {
                    if (fs.Length > 5 * 1024 * 1024)
                    {
                        fs.SetLength(0);
                    }
                    await writer.WriteAsync(message);
                }
                catch { }
            }
        }
        catch { }
    }

    public static void Log(string context, Exception ex)
    {
        try
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR in {context}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}";
            _logChannel.Writer.TryWrite(logMessage);
        }
        catch { }
    }

}