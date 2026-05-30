using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Services;
namespace WinPilot.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SystemInfoService _sysInfo = new();

    public DashboardViewModel    Dashboard       { get; }
    public SystemInfoViewModel   SystemInfo      { get; }
    public EventViewerViewModel  EventViewer     { get; } = new();
    public RecoveryViewModel     Recovery        { get; } = new();
    public PingViewModel         Ping            { get; } = new();
    public RegistryViewModel     Registry        { get; } = new();
    public SettingsViewModel     Settings        { get; } = SettingsViewModel.Current;

    [ObservableProperty] private object _currentPage = null!;
    [ObservableProperty] private bool _isSidebarExpanded = true;
    [ObservableProperty] private bool _isMiniMode;

    public bool IsNormalMode => !IsMiniMode;
    public MiniViewModel MiniVm { get; }

    public MainViewModel()
    {
        Dashboard  = new DashboardViewModel(_sysInfo);
        SystemInfo = new SystemInfoViewModel(_sysInfo);
        MiniVm = new MiniViewModel(_sysInfo);
        CurrentPage = Dashboard;
        Dashboard.StartAutoRefresh();
    }

    partial void OnIsMiniModeChanged(bool value) => OnPropertyChanged(nameof(IsNormalMode));

    [RelayCommand]
    private void NavigateTo(object? vm)
    {
        if (vm == null) return;
        CurrentPage = vm;
        if (vm is SystemInfoViewModel si)  _ = si.LoadAsync();
        if (vm is EventViewerViewModel ev && ev.Entries.Count == 0) _ = ev.RefreshAsync();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void ToggleMiniMode()
    {
        IsMiniMode = !IsMiniMode;
        if (IsMiniMode)
            MiniVm.StartAutoRefresh();
        else
            MiniVm.StopAutoRefresh();
    }
}
