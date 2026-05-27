using CommunityToolkit.Mvvm.ComponentModel;

namespace WinPilot.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public static SettingsViewModel Current { get; } = new();

    [ObservableProperty] private int _refreshIntervalSeconds = 30;
    [ObservableProperty] private bool _isSidebarExpanded = true;

    public IList<int> RefreshIntervals { get; } = [5, 10, 30, 60, 120, 300];
}
