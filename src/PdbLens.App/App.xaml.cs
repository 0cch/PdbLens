using Microsoft.UI.Xaml;

namespace PdbLens.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pdbPaths = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _window = new MainWindow(pdbPaths);
        _window.Activate();
    }
}
