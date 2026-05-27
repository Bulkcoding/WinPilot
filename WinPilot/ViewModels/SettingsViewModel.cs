using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinPilot.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public static SettingsViewModel Current { get; } = new();

    [ObservableProperty] private bool _isDarkTheme = true;

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
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }
        else
        {
            res["BgBrush"]            = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            res["SurfaceBrush"]       = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["SidebarBrush"]       = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextPrimaryBrush"]   = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }
    }
}
