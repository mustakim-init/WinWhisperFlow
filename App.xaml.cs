namespace WinWhisperFlow;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);


        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n{args.Exception.Message}",
                "WinWhisper Flow",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
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
}
