using System.Net.Sockets;
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
    public ProcessManagerViewModel ProcessManager { get; } = new();
    public RecoveryViewModel     Recovery        { get; } = new();
    public PingViewModel         Ping            { get; } = new();
    public DefenderViewModel     Defender        { get; } = new();
    public RegistryViewModel     Registry        { get; } = new();
    public SettingsViewModel     Settings        { get; } = SettingsViewModel.Current;

    [ObservableProperty] private object _currentPage = null!;
    [ObservableProperty] private bool   _isSidebarExpanded = true;
    [ObservableProperty] private bool   _isMiniMode;

    // 업데이트
    [ObservableProperty] private bool         _updateAvailable;
    [ObservableProperty] private string       _latestVersion = "";
    [ObservableProperty] private string       _updateDownloadUrl = "";
    [ObservableProperty] private bool         _isDownloading;
    [ObservableProperty] private int          _downloadProgress;
    [ObservableProperty] private bool         _showUpdatePopup;
    [ObservableProperty] private List<string> _updateReleaseNotes = [];

    // 버튼 표시 조건: 다운로드 완료 후 적용 가능
    public bool UpdateReady => UpdateAvailable && !IsDownloading;

    partial void OnIsDownloadingChanged(bool value)  => OnPropertyChanged(nameof(UpdateReady));
    partial void OnUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(UpdateReady));

    public bool IsNormalMode => !IsMiniMode;
    public MiniViewModel MiniVm { get; }
    public string LocalIpAddress  { get; } = GetLocalIp();
    // Run.Text에 Binding 불가 → 미리 조합된 문자열 사용
    public string SystemStatusText => $"시스템 정상({GetLocalIp()})";

    public MainViewModel()
    {
        Dashboard  = new DashboardViewModel(_sysInfo);
        SystemInfo = new SystemInfoViewModel(_sysInfo);
        MiniVm     = new MiniViewModel(_sysInfo);
        CurrentPage = Dashboard;
        Dashboard.StartAutoRefresh();

        // 시작 시 백그라운드 업데이트 체크 + 자동 다운로드
        _ = CheckAndAutoDownloadUpdateAsync();
    }

    private async Task CheckAndAutoDownloadUpdateAsync()
    {
        var info = await UpdateService.CheckAsync();
        if (info == null || string.IsNullOrEmpty(info.DownloadUrl)) return;

        LatestVersion     = info.Version;
        UpdateDownloadUrl = info.DownloadUrl;

        // 자동 다운로드 (백그라운드, autoApply=false → 버튼만 표시)
        IsDownloading = true;
        try
        {
            await UpdateService.DownloadAndApplyAsync(info, progress: null, autoApply: false);
            UpdateAvailable     = true;
            UpdateReleaseNotes  = info.ReleaseNotes;
            ShowUpdatePopup     = true;
        }
        catch { /* 네트워크 없으면 무시 */ }
        finally { IsDownloading = false; }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (string.IsNullOrEmpty(UpdateDownloadUrl)) return;
        var info = await UpdateService.CheckAsync();   // 최신 정보 재확인
        if (info == null) return;

        IsDownloading = true;
        var prog = new Progress<int>(v => DownloadProgress = v);
        try
        {
            await UpdateService.DownloadAndApplyAsync(info, prog, autoApply: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"업데이트 실패: {ex.Message}", "WinPilot",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            IsDownloading = false;
        }
    }

    partial void OnIsMiniModeChanged(bool value) => OnPropertyChanged(nameof(IsNormalMode));

    private static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "N/A";
        }
        catch { return "N/A"; }
    }

    [RelayCommand]
    private void DismissUpdatePopup() => ShowUpdatePopup = false;

    [RelayCommand]
    private void ReopenUpdatePopup() => ShowUpdatePopup = true;

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
