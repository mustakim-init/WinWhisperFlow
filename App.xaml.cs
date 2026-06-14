using System.IO;

namespace WinWhisperFlow;

public partial class App : System.Windows.Application
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinWhisperFlow", "logs", "crash.log");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n{args.Exception.Message}",
                "WinWhisper Flow",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogCrash(ex);
            System.Windows.MessageBox.Show(
                $"A fatal error occurred:\n{(args.ExceptionObject as System.Exception)?.Message}",
                "WinWhisper Flow",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };

        var window = new MainWindow();
        MainWindow = window;

        if (e.Args.Any(arg => string.Equals(arg, "--minimized", System.StringComparison.OrdinalIgnoreCase)))
        {
            window.Hide();
            window.SetStartMinimized();
            return;
        }

        window.Show();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
