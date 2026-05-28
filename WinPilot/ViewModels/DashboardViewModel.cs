using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Models;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly SystemInfoService _sysInfo;
    private readonly EventLogService _eventLog = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _cpuTimer;
    private readonly Queue<double> _ramHistory = new();
    private const int MaxHistory = 60;

    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private float _ramUsagePercent;
    [ObservableProperty] private string _ramText = "로딩 중...";
    [ObservableProperty] private string _uptimeText = "";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private List<DiskInfo> _disks = [];
    [ObservableProperty] private PointCollection _sparklinePoints = [];
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _infoCount;
    [ObservableProperty] private bool _isLoading = true;

    public DashboardViewModel(SystemInfoService sysInfo)
    {
        _sysInfo = sysInfo;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        _cpuTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cpuTimer.Tick += async (_, _) =>
        {
            CpuUsage = await _sysInfo.GetCpuUsageAsync();
            var ram = await _sysInfo.GetRamUsagePercentAsync();
            if (ram > 0) UpdateSparkline(ram);
        };
    }

    public async void StartAutoRefresh()
    {
        await Task.Delay(800);
        await RefreshAsync();
        _timer.Start();
        _cpuTimer.Start();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        var snap = await _sysInfo.GetSnapshotAsync();
        CpuUsage = snap.CpuUsage;
        RamUsagePercent = snap.RamUsagePercent;
        RamText = snap.RamText;
        UptimeText = snap.UptimeText;
        OsVersion = snap.OsVersion;
        CpuName = snap.CpuName;
        Disks = _sysInfo.GetDiskInfo();
        UpdateSparkline(snap.RamUsagePercent);

        var summary = await Task.Run(() =>
            _eventLog.GetSummary(DateTime.Today.AddDays(-1), DateTime.Now.AddHours(1)));
        ErrorCount = summary.Errors;
        WarningCount = summary.Warnings;
        InfoCount = summary.Infos;
        IsLoading = false;
    }

    [RelayCommand]
    private void RunSfc()
    {
        if (!RecoveryService.IsRunningAsAdmin())
        {
            MessageBox.Show("SFC 실행에는 관리자 권한이 필요합니다.\n복구 도구 탭에서 '관리자로 재시작'을 클릭하세요.",
                "WinPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show($"SFC 실행 실패: {ex.Message}", "WinPilot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSparkline(double ramPercent)
    {
        _ramHistory.Enqueue(ramPercent);
        if (_ramHistory.Count > MaxHistory) _ramHistory.Dequeue();

        const double w = 300, h = 56;
        var values = _ramHistory.ToArray();
        var pts = new PointCollection();
        for (int i = 0; i < values.Length; i++)
        {
            double x = values.Length > 1 ? (double)i / (values.Length - 1) * w : 0;
            double y = h - values[i] / 100.0 * h;
            pts.Add(new System.Windows.Point(x, y));
        }
        SparklinePoints = pts;
    }
}
