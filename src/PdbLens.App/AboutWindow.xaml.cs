using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Windows.Graphics;
using WinRT.Interop;

namespace PdbLens.App;

public sealed partial class AboutWindow : Window
{
    private const int GwlpHwndParent = -8;

    public AboutWindow(Window owner, ElementTheme theme)
    {
        InitializeComponent();
        RootGrid.RequestedTheme = theme;
        ConfigureTitleBar(theme);
        VersionText.Text = $"Version {GetVersion()}";
        ResizeAndCenter(owner);
    }

    public void ApplyTheme(ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
        TitleBarThemeHelper.Apply(this, theme);
    }

    private void ConfigureTitleBar(ElementTheme theme)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                AboutTitleBar.Visibility = Visibility.Collapsed;
                TitleBarRow.Height = new GridLength(0);
                return;
            }

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AboutTitleBar);
            TitleBarThemeHelper.Apply(this, theme);
        }
        catch
        {
            AboutTitleBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
        }
    }

    private static string GetVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            var informationalVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+', 2)[0];
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "Unknown" : version.ToString(4);
        }
    }

    private void ResizeAndCenter(Window owner)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var ownerHwnd = WindowNative.GetWindowHandle(owner);
        SetWindowLongPtrW(hwnd, GwlpHwndParent, ownerHwnd);

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var ownerWindowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var ownerAppWindow = AppWindow.GetFromWindowId(ownerWindowId);
        var width = 560;
        var height = 340;
        appWindow.Resize(new SizeInt32(width, height));

        var ownerPosition = ownerAppWindow.Position;
        var ownerSize = ownerAppWindow.Size;
        appWindow.Move(new PointInt32(
            ownerPosition.X + Math.Max(0, (ownerSize.Width - width) / 2),
            ownerPosition.Y + Math.Max(0, (ownerSize.Height - height) / 2)));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int index, nint newValue);
}
