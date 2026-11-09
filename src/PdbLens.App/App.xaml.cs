using Microsoft.UI.Xaml;

namespace PdbLens.App;

public partial class App : Application
{
    private static readonly object CrashLogLock = new();
    private Window? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        UnhandledException += App_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pdbPaths = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _window = new MainWindow(pdbPaths);
        _window.Activate();
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        WriteCrashLog("WinUI", e.Exception);

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e) =>
        WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PdbLens");
            Directory.CreateDirectory(directory);
            var message = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            lock (CrashLogLock)
            {
                File.AppendAllText(Path.Combine(directory, "crash.log"), message);
            }
        }
        catch
        {
            // Logging must never hide or replace the original failure.
        }
    }
}
