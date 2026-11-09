using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using WinRT.Interop;

namespace PdbLens.App;

internal static class TitleBarThemeHelper
{
    public static void Apply(Window window, ElementTheme theme)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            var dark = theme == ElementTheme.Dark;
            var foreground = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 28, 28, 28);
            var inactiveForeground = dark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(150, 28, 28, 28);
            var hoverBackground = dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
            var pressedBackground = dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = inactiveForeground;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
            titleBar.ButtonPressedForegroundColor = foreground;
        }
        catch
        {
            // Color customization is optional; the system title bar remains usable.
        }
    }

    public static void ApplyInsets(Window window, Grid titleBarElement)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;
            titleBarElement.Padding = new Thickness(
                titleBar.LeftInset,
                0,
                titleBar.RightInset,
                0);
        }
        catch
        {
        }
    }
}
