using Microsoft.UI.Xaml;

namespace PdbLens.App;

internal enum AppThemePreference
{
    System,
    Light,
    Dark
}

internal static class ThemePreferenceStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdbLens",
        "theme.txt");

    public static AppThemePreference Load()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                Enum.TryParse<AppThemePreference>(File.ReadAllText(SettingsPath).Trim(), true, out var value))
            {
                return value;
            }
        }
        catch
        {
        }

        return AppThemePreference.System;
    }

    public static void Save(AppThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, preference.ToString());
        }
        catch
        {
        }
    }

    public static ElementTheme ToElementTheme(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
