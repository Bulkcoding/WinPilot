using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

// ─── 데이터 모델 ──────────────────────────────────────────────────────────────

public record ProcessRow(
    string Name, int Pid, double CpuPercent, double MemoryMB,
    string Status, string Description, string UserName, string WindowTitle);

public record AppHistoryRow(
    string Name, string CpuTimeText, double PeakMemoryMB, int InstanceCount);

public record ProcessDetailRow(
    string Name, int Pid, string Status, double CpuPercent, double MemoryMB,
    string UserName, string Description, string Path);

/// <summary>프로세스 탭의 그룹 헤더 또는 자식 행</summary>
public partial class ProcessGroupItem : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;

    // 공통
    public string Name        { get; init; } = "";
    public double CpuPercent  { get; init; }
    public double MemoryMB    { get; init; }
    public string Status      { get; init; } = "";
    public string Description { get; init; } = "";

    // 그룹 전용
    public bool   IsGroup     { get; init; }
    public int    ChildCount  { get; init; } = 1;
    public List<ProcessGroupItem> Children { get; init; } = [];

    // 자식 전용
    public int    Pid         { get; init; }
    public string ParentName  { get; init; } = "";

    // 계산 프로퍼티
    public bool   IsChild      => !IsGroup;
    public bool   HasChildren  => IsGroup && ChildCount > 1;
    public string PidDisplay   => IsChild  ? Pid.ToString()
                                : ChildCount == 1 ? (Children.FirstOrDefault()?.Pid.ToString() ?? "-")
                                : $"{ChildCount}개";
}

public partial class ServiceRow : ObservableObject
{
    public string Name        { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    [ObservableProperty] private string _state = "";
    public string StartMode   { get; init; } = "";
    public int    Pid         { get; init; }
    public bool   IsRunning   => State == "Running";
    public bool   IsStopped   => State == "Stopped";

    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
    }
}

// ─── ViewModel ───────────────────────────────────────────────────────────────

public partial class ProcessManagerViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private Dictionary<int, (DateTime time, TimeSpan cpuTime)> _prevCpu = [];
    private static readonly int CpuCount = Environment.ProcessorCount;

    [ObservableProperty] private int    _selectedTab;
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _processFilter  = "";
    [ObservableProperty] private string _serviceFilter  = "";
    [ObservableProperty] private string _statusText     = "";

    // 탭별 선택 항목 (헤더 버튼이 바인딩)
    [ObservableProperty] private ProcessGroupItem? _selectedGroupItem;
    [ObservableProperty] private ProcessDetailRow? _selectedDetail;
    [ObservableProperty] private ServiceRow?       _selectedService;

    // 헤더 버튼 표시 여부
    public bool ShowEndTask    => SelectedTab is 0 or 2;
    public bool ShowServiceOps => SelectedTab == 3;

    // 그룹화된 flat list (그룹헤더 + 확장 시 자식 행)
    private List<ProcessGroupItem> _allGroups = [];
    public ObservableCollection<ProcessGroupItem>  FlatGroups  { get; } = [];
    public ObservableCollection<AppHistoryRow>    AppHistory  { get; } = [];
    public ObservableCollection<ProcessDetailRow> Details     { get; } = [];
    public ObservableCollection<ServiceRow>       Services    { get; } = [];

    public ProcessManagerViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshCurrentAsync();
        _ = RefreshCurrentAsync();
        _timer.Start();
    }

    partial void OnSelectedTabChanged(int value)
    {
        _ = RefreshCurrentAsync();
        OnPropertyChanged(nameof(ShowEndTask));
        OnPropertyChanged(nameof(ShowServiceOps));
    }
    partial void OnProcessFilterChanged(string value) => _ = RefreshCurrentAsync();
    partial void OnServiceFilterChanged(string value) => _ = RefreshCurrentAsync();

    [RelayCommand]
    public async Task RefreshAsync() => await RefreshCurrentAsync();

    private async Task RefreshCurrentAsync()
    {
        IsLoading = true;
        switch (SelectedTab)
        {
            case 0: await RefreshProcessesAsync();   break;
            case 1: await RefreshAppHistoryAsync();  break;
            case 2: await RefreshDetailsAsync();     break;
            case 3: await RefreshServicesAsync();    break;
        }
        IsLoading = false;
    }

    // ─── 프로세스 (그룹화) ───────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleGroup(ProcessGroupItem? item)
    {
        if (item == null || !item.HasChildren) return;
        item.IsExpanded = !item.IsExpanded;
        RebuildFlatList();
    }

    private void RebuildFlatList()
    {
        var newItems = new List<ProcessGroupItem>();
        foreach (var group in _allGroups)
        {
            newItems.Add(group);
            if (group.IsExpanded)
                newItems.AddRange(group.Children);
        }
        UpdateCollection(FlatGroups, newItems);
    }

    private async Task RefreshProcessesAsync()
    {
        var filter = ProcessFilter.Trim();
        var (rows, newCpu) = await Task.Run(() => BuildProcessRows());
        _prevCpu = newCpu;

        // 현재 확장된 그룹 이름 저장
        var expanded = _allGroups.Where(g => g.IsExpanded).Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 그룹 재빌드 (확장 상태 복원)
        var groups = BuildGroupItems(rows, expanded);

        // 필터 적용
        _allGroups = filter.Length > 0
            ? groups.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()
            : groups;

        RebuildFlatList();

        var total = _allGroups.Sum(g => g.ChildCount);
        StatusText = $"{_allGroups.Count}개 그룹 ({total}개 프로세스)";
    }

    private static List<ProcessGroupItem> BuildGroupItems(List<ProcessRow> rows, HashSet<string>? expanded = null)
    {
        return rows
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var children = g
                    .OrderByDescending(r => r.CpuPercent)
                    .Select(r => new ProcessGroupItem
                    {
                        Name        = r.Name,
                        CpuPercent  = r.CpuPercent,
                        MemoryMB    = r.MemoryMB,
                        Status      = r.Status,
                        Description = r.Description,
                        Pid         = r.Pid,
                        ParentName  = g.Key
                    }).ToList();

                return new ProcessGroupItem
                {
                    IsGroup     = true,
                    Name        = g.Key,
                    ChildCount  = children.Count,
                    CpuPercent  = Math.Round(children.Sum(r => r.CpuPercent), 1),
                    MemoryMB    = Math.Round(children.Sum(r => r.MemoryMB), 1),
                    Status      = children.All(r => r.Status == "실행 중") ? "실행 중" : "혼합",
                    Description = children.FirstOrDefault(r => !string.IsNullOrEmpty(r.Description))?.Description ?? "",
                    Children    = children,
                    IsExpanded  = expanded?.Contains(g.Key) ?? false
                };
            })
            .OrderByDescending(g => g.CpuPercent)
            .ToList();
    }

    private (List<ProcessRow> rows, Dictionary<int, (DateTime, TimeSpan)> cpuMap) BuildProcessRows()
    {
        var now     = DateTime.UtcNow;
        var cpuMap  = new Dictionary<int, (DateTime, TimeSpan)>();
        var rows    = new List<ProcessRow>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var cpuTime = p.TotalProcessorTime;
                cpuMap[p.Id] = (now, cpuTime);

                double cpu = 0;
                if (_prevCpu.TryGetValue(p.Id, out var prev))
                {
                    var elapsed = (now - prev.time).TotalSeconds;
                    if (elapsed > 0.1)
                        cpu = Math.Clamp((cpuTime - prev.cpuTime).TotalSeconds / (elapsed * CpuCount) * 100, 0, 100);
                }

                rows.Add(new ProcessRow(
                    Name:        p.ProcessName,
                    Pid:         p.Id,
                    CpuPercent:  Math.Round(cpu, 1),
                    MemoryMB:    Math.Round(p.PrivateMemorySize64 / 1_048_576.0, 1),
                    Status:      p.Responding ? "실행 중" : "응답 없음",
                    Description: SafeFileDescription(p),
                    UserName:    "",   // 별도 WMI 조회 생략 (성능)
                    WindowTitle: p.MainWindowTitle));
            }
            catch { /* 접근 거부 or 프로세스 종료 */ }
        }
        return (rows, cpuMap);
    }

    [RelayCommand]
    private void EndTask()
    {
        if (SelectedTab == 2)
        {
            // 자세히 탭 — 단일 프로세스 종료
            KillSingle(SelectedDetail?.Name ?? "", SelectedDetail?.Pid ?? -1);
        }
        else
        {
            var item = SelectedGroupItem;
            if (item == null) return;

            if (item.HasChildren)
            {
                // 다중 인스턴스 그룹 — 전체 종료 확인
                var r = MessageBox.Show(
                    $"'{item.Name}' 그룹의 프로세스 {item.ChildCount}개를 모두 종료하시겠습니까?",
                    "WinPilot — 작업 끝내기", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
                foreach (var child in item.Children)
                    TryKillPid(child.Pid);
                _ = RefreshCurrentAsync();
            }
            else
            {
                // 단일 인스턴스 그룹 또는 자식 행
                var pid = item.IsChild ? item.Pid
                        : item.Children.FirstOrDefault()?.Pid ?? -1;
                KillSingle(item.Name, pid);
            }
        }
    }

    private void KillSingle(string name, int pid)
    {
        if (pid < 0) return;
        var r = MessageBox.Show(
            $"'{name}' (PID {pid}) 를 종료하시겠습니까?",
            "WinPilot — 작업 끝내기", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        TryKillPid(pid);
        _ = RefreshCurrentAsync();
    }

    private static void TryKillPid(int pid)
    {
        try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
        catch (Exception ex)
        {
            MessageBox.Show($"종료 실패: {ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── 앱 기록 ─────────────────────────────────────────────────────────────

    private async Task RefreshAppHistoryAsync()
    {
        var rows = await Task.Run(BuildAppHistoryRows);
        var sorted = rows.OrderByDescending(r => r.InstanceCount).ToList();
        UpdateCollection(AppHistory, sorted);
        StatusText = $"{sorted.Count}개 앱";
    }

    private static List<AppHistoryRow> BuildAppHistoryRows()
    {
        var groups = new Dictionary<string, (TimeSpan cpu, double mem, int count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var key = p.ProcessName;
                var cpu = p.TotalProcessorTime;
                var mem = p.PrivateMemorySize64 / 1_048_576.0;
                if (groups.TryGetValue(key, out var g))
                    groups[key] = (g.cpu + cpu, Math.Max(g.mem, mem), g.count + 1);
                else
                    groups[key] = (cpu, mem, 1);
            }
            catch { }
        }
        return groups
            .Select(kv => new AppHistoryRow(
                Name:         kv.Key,
                CpuTimeText:  $"{(int)kv.Value.cpu.TotalHours:D2}:{kv.Value.cpu.Minutes:D2}:{kv.Value.cpu.Seconds:D2}",
                PeakMemoryMB: Math.Round(kv.Value.mem, 1),
                InstanceCount: kv.Value.count))
            .ToList();
    }

    // ─── 자세히 ──────────────────────────────────────────────────────────────

    private async Task RefreshDetailsAsync()
    {
        var filter = ProcessFilter.Trim();
        var (rawRows, newCpu) = await Task.Run(() => BuildProcessRows());
        _prevCpu = newCpu;

        // WMI로 Username 매핑 (별도 비동기)
        var userMap = await Task.Run(BuildUserNameMap);

        var rows = rawRows
            .Where(r => filter.Length == 0 ||
                        r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        r.Pid.ToString().Contains(filter))
            .Select(r => new ProcessDetailRow(
                Name:        r.Name,
                Pid:         r.Pid,
                Status:      r.Status,
                CpuPercent:  r.CpuPercent,
                MemoryMB:    r.MemoryMB,
                UserName:    userMap.GetValueOrDefault(r.Pid, ""),
                Description: r.Description,
                Path:        SafeProcessPath(r.Pid)))
            .OrderBy(r => r.Name)
            .ToList();

        UpdateCollection(Details, rows);
        StatusText = $"{rows.Count}개 항목";
    }

    private static Dictionary<int, string> BuildUserNameMap()
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var args   = new object[] { "", "" };
                    obj.InvokeMethod("GetOwner", args);
                    var user   = args[0]?.ToString() ?? "";
                    var domain = args[1]?.ToString() ?? "";
                    if (int.TryParse(obj["ProcessId"]?.ToString(), out int pid))
                        map[pid] = domain.Length > 0 ? $@"{domain}\{user}" : user;
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    // ─── 서비스 ──────────────────────────────────────────────────────────────

    private async Task RefreshServicesAsync()
    {
        var filter = ServiceFilter.Trim().ToLower();
        var list   = await Task.Run(QueryServices);

        var filtered = filter.Length > 0
            ? list.Where(s => s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                           || s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()
            : list;

        UpdateCollection(Services, filtered.OrderBy(s => s.DisplayName).ToList());
        StatusText = $"{filtered.Count}개 서비스";
    }

    private static List<ServiceRow> QueryServices()
    {
        var rows = new List<ServiceRow>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,DisplayName,Description,State,StartMode,ProcessId FROM Win32_Service");
            foreach (ManagementObject obj in searcher.Get())
            {
                rows.Add(new ServiceRow
                {
                    Name        = obj["Name"]?.ToString()        ?? "",
                    DisplayName = obj["DisplayName"]?.ToString() ?? "",
                    Description = obj["Description"]?.ToString() ?? "",
                    State       = obj["State"]?.ToString()       ?? "",
                    StartMode   = obj["StartMode"]?.ToString()   ?? "",
                    Pid         = obj["ProcessId"] is uint pid ? (int)pid : 0
                });
            }
        }
        catch { }
        return rows;
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        if (SelectedService == null) return;
        await RunServiceMethodAsync(SelectedService.Name, "StartService");
        await RefreshServicesAsync();
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        if (SelectedService == null) return;
        var r = MessageBox.Show($"'{SelectedService.DisplayName}' 서비스를 중지하시겠습니까?",
            "WinPilot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        await RunServiceMethodAsync(SelectedService.Name, "StopService");
        await RefreshServicesAsync();
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        if (SelectedService == null) return;
        await RunServiceMethodAsync(SelectedService.Name, "StopService");
        await Task.Delay(1500);
        await RunServiceMethodAsync(SelectedService.Name, "StartService");
        await RefreshServicesAsync();
    }

    private static Task RunServiceMethodAsync(string name, string method) => Task.Run(() =>
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Service WHERE Name='{name.Replace("'", "\\'")}'");
            foreach (ManagementObject obj in searcher.Get())
                obj.InvokeMethod(method, null);
        }
        catch { }
    });

    // ─── 유틸 ────────────────────────────────────────────────────────────────

    private static string SafeFileDescription(Process p)
    {
        try { return p.MainModule?.FileVersionInfo?.FileDescription ?? ""; }
        catch { return ""; }
    }

    private static string SafeProcessPath(int pid)
    {
        try { return Process.GetProcessById(pid).MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    /// 기존 컬렉션을 덮어쓰되 UI 바인딩을 갱신합니다.
    private static void UpdateCollection<T>(ObservableCollection<T> col, IList<T> newItems)
    {
        col.Clear();
        foreach (var item in newItems) col.Add(item);
    }
}
