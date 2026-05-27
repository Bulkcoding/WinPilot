using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Models;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class SystemInfoViewModel : ObservableObject
{
    private readonly SystemInfoService _sysInfo;

    [ObservableProperty] private SystemSnapshot? _snapshot;
    [ObservableProperty] private List<DiskInfo> _disks = [];
    [ObservableProperty] private List<NetworkInfo> _networks = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _copyStatusText = "";

    public SystemInfoViewModel(SystemInfoService sysInfo)
    {
        _sysInfo = sysInfo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Snapshot = await _sysInfo.GetSnapshotAsync();
        Disks = _sysInfo.GetDiskInfo();
        Networks = _sysInfo.GetNetworkInfo();
        IsLoading = false;
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (Snapshot == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"=== WinPilot 시스템 정보 ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===");
        sb.AppendLine($"컴퓨터: {Snapshot.ComputerName}  /  사용자: {Snapshot.UserName}");
        sb.AppendLine($"OS: {Snapshot.OsVersion} (빌드: {Snapshot.OsBuild}, {Snapshot.SystemType})");
        sb.AppendLine($"CPU: {Snapshot.CpuName} @ {Snapshot.CpuSpeedGhz:F2} GHz  ({Snapshot.CpuCores}코어 / {Snapshot.CpuThreads}스레드)");
        sb.AppendLine($"메모리: {Snapshot.RamText}  ({Snapshot.RamUsagePercent:F1}% 사용)");
        sb.AppendLine($"가동 시간: {Snapshot.UptimeText}");
        sb.AppendLine();
        sb.AppendLine("--- 디스크 ---");
        foreach (var d in Disks)
            sb.AppendLine($"  {d.DisplayName,-20} {d.UsedText,8} / {d.TotalText,8}  ({d.UsagePercent:F1}% 사용)");
        sb.AppendLine();
        sb.AppendLine("--- 네트워크 ---");
        foreach (var n in Networks)
            sb.AppendLine($"  {n.AdapterName,-20} {n.IpAddress,-15}  {n.StatusText}");

        Clipboard.SetText(sb.ToString());
        CopyStatusText = "클립보드에 복사됨!";
        Task.Delay(3000).ContinueWith(_ => CopyStatusText = "", TaskScheduler.FromCurrentSynchronizationContext());
    }
}
