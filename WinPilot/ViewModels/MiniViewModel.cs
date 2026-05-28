using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class MiniViewModel : ObservableObject
{
    private readonly SystemInfoService _sysInfo;
    private readonly EventLogService _eventLog = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _cpuTimer;

    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private float _ramUsagePercent;
    [ObservableProperty] private string _ramText = "";
    [ObservableProperty] private string _uptimeText = "";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private int _eventCount;

    public MiniViewModel(SystemInfoService sysInfo)
    {
        _sysInfo = sysInfo;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _cpuTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cpuTimer.Tick += async (_, _) => CpuUsage = await _sysInfo.GetCpuUsageAsync();
    }

    public async void StartAutoRefresh()
    {
        await RefreshAsync();
        _timer.Start();
        _cpuTimer.Start();
    }

    public void StopAutoRefresh()
    {
        _timer.Stop();
        _cpuTimer.Stop();
    }

    private async Task RefreshAsync()
    {
        var snap = await _sysInfo.GetSnapshotAsync();
        CpuUsage = snap.CpuUsage;
        RamUsagePercent = snap.RamUsagePercent;
        RamText = snap.RamText;
        UptimeText = snap.UptimeText;
        OsVersion = snap.OsVersion;

        var summary = await Task.Run(() =>
            _eventLog.GetSummary(DateTime.Today.AddDays(-1), DateTime.Now.AddHours(1)));
        EventCount = summary.Errors + summary.Warnings;
    }

    [RelayCommand]
    private void RunSfc()
    {
        if (!RecoveryService.IsRunningAsAdmin())
        {
            MessageBox.Show("SFC 실행에는 관리자 권한이 필요합니다.", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k sfc /scannow",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"SFC 실행 실패: {ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
