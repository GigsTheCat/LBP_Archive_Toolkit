using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LbpArchiveToolkit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
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
    private static readonly System.Threading.Lock LockObj = new();

    public static void Log(string context, Exception ex)
        {
            try
            {
                lock (LockObj)
                {
                    Directory.CreateDirectory(AppDataFolder);
                    
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024)
                        File.Delete(LogPath);

                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR in {context}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
                }
            }
        catch
        {
            // Fail silently to prevent recursive logging failures
        }
    }

    public static void LogWarning(string context, string message)
        {
            try
            {
                lock (LockObj)
                {
                    Directory.CreateDirectory(AppDataFolder);
                    
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024)
                        File.Delete(LogPath);

                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARNING in {context}: {message}{Environment.NewLine}{Environment.NewLine}");
                }
            }
        catch
        {
            // Fail silently
        }
    }
}