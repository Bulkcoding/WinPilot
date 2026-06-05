using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public static SettingsViewModel Current { get; } = new();

    [ObservableProperty] private bool _isDarkTheme = true;

    public string CurrentVersionText => UpdateService.CurrentVersionText;

    partial void OnIsDarkThemeChanged(bool value) => ApplyTheme(value);

    public static void ApplyTheme(bool isDark)
    {
        var res = Application.Current.Resources;
        if (isDark)
        {
            res["BgBrush"]            = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            res["SurfaceBrush"]       = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3E));
            res["SidebarBrush"]       = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x2A));
            res["TextPrimaryBrush"]   = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            res["TextValueBrush"]     = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            res["ItemRowBrush"]       = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2A));
            res["AltRowBrush"]        = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x35));
            res["ControlBrush"]       = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            res["BorderBrush"]        = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            res["WarningBgBrush"]     = new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x10));
            res["ConsoleBgBrush"]     = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x1A));
            res["ConsoleTextBrush"]   = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }
        else
        {
            res["BgBrush"]            = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            res["SurfaceBrush"]       = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["SidebarBrush"]       = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextPrimaryBrush"]   = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextValueBrush"]     = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            res["ItemRowBrush"]       = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7));
            res["AltRowBrush"]        = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF5));
            res["ControlBrush"]       = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            res["BorderBrush"]        = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            res["WarningBgBrush"]     = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
            res["ConsoleBgBrush"]     = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            res["ConsoleTextBrush"]   = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }
    }
}
