using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

// ─── 데이터 모델 ──────────────────────────────────────────────────────────────

public record ProcessRow(
    string Name, int Pid, double CpuPercent, double MemoryMB,
    string Status, string Description, string UserName, string WindowTitle, string ExePath,
    bool HasWindow = false);

public record AppHistoryRow(
    string Name, string CpuTimeText, double PeakMemoryMB, int InstanceCount, ImageSource? IconSource = null);

public record ProcessDetailRow(
    string Name, int Pid, string Status, double CpuPercent, double MemoryMB,
    string UserName, string Description, string Path, ImageSource? IconSource = null);

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
    public int    GroupKey    { get; init; }   // 그룹 식별자 = 앱 트리 루트 PID (이름 대신 사용)
    public int    ChildCount  { get; init; } = 1;
    public bool   IsApp       { get; init; }   // 창 있는 앱이면 true → "앱" 섹션, 아니면 "백그라운드"
    public List<ProcessGroupItem> Children { get; init; } = [];

    // 섹션 헤더 전용 (앱 / 백그라운드 프로세스 구분 배너 행)
    public bool   IsSectionHeader { get; init; }
    public string SectionTitle    { get; init; } = "";

    // 자식 전용
    public int    Pid         { get; init; }
    public string ParentName  { get; init; } = "";

    public ImageSource? IconSource { get; init; }

    // 계산 프로퍼티
    public bool   IsChild      => !IsGroup;
    public bool   HasChildren  => IsGroup && ChildCount > 1;
    public string PidDisplay   => IsChild  ? Pid.ToString()
                                : ChildCount == 1 ? (Children.FirstOrDefault()?.Pid.ToString() ?? "-")
                                : $"{ChildCount}개";
}

public partial class ServiceRow : ObservableObject
{
    public string        Name        { get; init; } = "";
    public string        DisplayName { get; init; } = "";
    public string        Description { get; init; } = "";
    public ImageSource?  IconSource  { get; init; }
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
    private List<ProcessGroupItem> _allGroups  = [];
    private string _sortColumn      = "";   // "" = 무정렬(순서 보존)
    private bool   _sortDescending  = true;
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

    /// <summary>컬럼 헤더 클릭 시 그룹 단위 정렬</summary>
    [RelayCommand]
    private void SortBy(string? column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = true; }
        ApplySort();
        RebuildFlatList();
    }

    private void ApplySort()
    {
        if (string.IsNullOrEmpty(_sortColumn)) return; // 정렬 없음 → 현재 순서 유지

        Comparison<ProcessGroupItem> cmp = _sortColumn switch
        {
            "MemoryMB"   => (a, b) => _sortDescending ? b.MemoryMB.CompareTo(a.MemoryMB)   : a.MemoryMB.CompareTo(b.MemoryMB),
            "Name"       => (a, b) => _sortDescending ? string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase)
                                                       : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            "Status"     => (a, b) => _sortDescending ? string.Compare(b.Status, a.Status) : string.Compare(a.Status, b.Status),
            _            => (a, b) => _sortDescending ? b.CpuPercent.CompareTo(a.CpuPercent) : a.CpuPercent.CompareTo(b.CpuPercent),
        };
        _allGroups.Sort(cmp);

        // 그룹 내 자식도 동일 기준 정렬
        foreach (var g in _allGroups)
            g.Children.Sort(cmp);
    }

    [RelayCommand]
    private void ToggleGroup(ProcessGroupItem? item)
    {
        if (item == null || !item.HasChildren) return;
        item.IsExpanded = !item.IsExpanded;
        RebuildFlatList();
    }

    private void RebuildFlatList()
    {
        // "앱" 섹션이 항상 위에 오도록 앱 우선으로 안정 정렬 (섹션 내 정렬 순서는 유지)
        _allGroups = _allGroups.OrderByDescending(g => g.IsApp).ToList();

        var newItems = new List<ProcessGroupItem>();
        bool addedApp = false, addedBg = false;
        foreach (var group in _allGroups)
        {
            // 각 섹션 첫 그룹 앞에 헤더 배너 행 삽입 (작업관리자 스타일)
            if (group.IsApp && !addedApp)
            { newItems.Add(new ProcessGroupItem { IsSectionHeader = true, SectionTitle = "앱" }); addedApp = true; }
            else if (!group.IsApp && !addedBg)
            { newItems.Add(new ProcessGroupItem { IsSectionHeader = true, SectionTitle = "백그라운드 프로세스" }); addedBg = true; }

            newItems.Add(group);
            if (group.IsExpanded)
                newItems.AddRange(group.Children);
        }
        UpdateCollection(FlatGroups, newItems);
    }

    private async Task RefreshProcessesAsync()
    {
        var filter = ProcessFilter.Trim();
        var (rows, newCpu, rootOf) = await Task.Run(() =>
        {
            var (r, cpu) = BuildProcessRows();
            return (r, cpu, QueryTreeRoots());
        });
        _prevCpu = newCpu;

        // 자동 새로고침 후에도 선택을 유지하기 위해 현재 선택 식별자 저장
        // (그룹은 GroupKey=루트 PID, 자식 행은 Pid 로 식별)
        var selKey = SelectedGroupItem is { } sel
            ? ((bool isGroup, int id)?)(sel.IsGroup, sel.IsGroup ? sel.GroupKey : sel.Pid)
            : null;

        // 현재 확장된 그룹 저장 (그룹 식별자 = 루트 PID)
        var expanded = _allGroups.Where(g => g.IsExpanded).Select(g => g.GroupKey).ToHashSet();

        // 그룹 재빌드 (확장 상태 복원)
        var groups = BuildGroupItems(rows, rootOf, expanded);

        // 정렬이 없을 때: 이전 순서를 유지 (새 그룹은 맨 뒤에 추가)
        if (string.IsNullOrEmpty(_sortColumn) && _allGroups.Count > 0)
        {
            var prevOrder = _allGroups
                .Select((g, idx) => (g.GroupKey, idx))
                .ToDictionary(t => t.GroupKey, t => t.idx);
            groups.Sort((a, b) =>
            {
                var ai = prevOrder.TryGetValue(a.GroupKey, out var av) ? av : int.MaxValue;
                var bi = prevOrder.TryGetValue(b.GroupKey, out var bv) ? bv : int.MaxValue;
                return ai.CompareTo(bi);
            });
        }

        // 필터 적용
        _allGroups = filter.Length > 0
            ? groups.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()
            : groups;

        ApplySort();
        RebuildFlatList();   // 내부에서 앱 우선 정렬 후 flat list 재구성

        // 선택 복원: 컬렉션이 새 인스턴스로 교체됐어도 같은 그룹/프로세스를 다시 선택
        if (selKey is { } key)
        {
            var match = FlatGroups.FirstOrDefault(g =>
                g.IsGroup == key.isGroup && (key.isGroup ? g.GroupKey : g.Pid) == key.id);
            if (match != null) SelectedGroupItem = match;
        }

        var total = _allGroups.Sum(g => g.ChildCount);
        StatusText = $"{_allGroups.Count}개 그룹 ({total}개 프로세스)";
    }

    private static List<ProcessGroupItem> BuildGroupItems(
        List<ProcessRow> rows, Dictionary<int, int> rootOf, HashSet<int>? expanded = null)
    {
        return rows
            // 작업관리자처럼 앱 트리(루트 PID) 단위로 묶는다. 같은 이름이라도 별도 트리면 다른 그룹.
            .GroupBy(r => rootOf.TryGetValue(r.Pid, out var root) ? root : r.Pid)
            .Select(g =>
            {
                // 그룹 대표는 트리 루트 프로세스(없으면 CPU 최상위)
                var rootRow  = g.FirstOrDefault(r => r.Pid == g.Key) ?? g.First();
                var groupName = rootRow.Name;

                // 트리 내 하나라도 창이 있으면 "앱", 아니면 "백그라운드 프로세스" (작업관리자 분류)
                bool isApp = g.Any(r => r.HasWindow);

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
                        ParentName  = groupName,
                        IconSource  = GetIconForPath(r.ExePath)
                    }).ToList();

                return new ProcessGroupItem
                {
                    IsGroup     = true,
                    Name        = groupName,
                    GroupKey    = g.Key,
                    ChildCount  = children.Count,
                    IsApp       = isApp,
                    CpuPercent  = Math.Round(children.Sum(r => r.CpuPercent), 1),
                    MemoryMB    = Math.Round(children.Sum(r => r.MemoryMB), 1),
                    Status      = children.All(r => r.Status == "실행 중") ? "실행 중" : "혼합",
                    Description = children.FirstOrDefault(r => !string.IsNullOrEmpty(r.Description))?.Description ?? "",
                    Children    = children,
                    IsExpanded  = expanded?.Contains(g.Key) ?? false,
                    IconSource  = GetIconForPath(rootRow.ExePath)
                };
            })
            .OrderByDescending(g => g.CpuPercent)
            .ToList();
    }

    /// <summary>
    /// 작업관리자 "메모리" 열과 동일한 값(활성 개인 작업 집합, Active Private Working Set)을
    /// PID → MB 로 한 번에 조회한다. .NET 의 PrivateMemorySize64(Private Bytes) 나
    /// WorkingSet64(공유 포함 워킹셋) 는 작업관리자와 다른 값이라 WMI 성능 카운터를 쓴다.
    /// </summary>
    private static Dictionary<int, double> QueryPrivateWorkingSet()
    {
        var map = new Dictionary<int, double>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT IDProcess, WorkingSetPrivate FROM Win32_PerfRawData_PerfProc_Process");
            foreach (ManagementObject mo in searcher.Get())
            {
                try
                {
                    int    pid   = Convert.ToInt32(mo["IDProcess"]);
                    double bytes = Convert.ToDouble(mo["WorkingSetPrivate"]);
                    map[pid] = bytes / 1_048_576.0;
                }
                catch { /* 개별 항목 파싱 실패 무시 */ }
            }
        }
        catch { /* WMI 조회 실패 시 빈 맵 → 호출부에서 WorkingSet64 로 폴백 */ }
        return map;
    }

    /// <summary>
    /// 각 프로세스를 "앱 트리 루트" PID 로 매핑한다 (PID → 루트 PID).
    /// 작업관리자가 애플리케이션(프로세스 트리) 단위로 묶는 것과 맞추기 위함.
    /// 부모가 같은 이름이면 계속 위로 올라가고, 부모 이름이 달라지는 지점이 루트다.
    /// (예: claude→claude→claude 는 한 트리, 부모가 터미널/탐색기면 거기서 끊겨 별도 트리가 됨)
    /// </summary>
    private static Dictionary<int, int> QueryTreeRoots()
    {
        var parent = new Dictionary<int, int>();
        var name   = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name FROM Win32_Process");
            foreach (ManagementObject mo in searcher.Get())
            {
                try
                {
                    int pid = Convert.ToInt32(mo["ProcessId"]);
                    parent[pid] = Convert.ToInt32(mo["ParentProcessId"]);
                    name[pid]   = (mo["Name"] as string) ?? "";
                }
                catch { /* 개별 항목 무시 */ }
            }
        }
        catch { /* WMI 실패 → 빈 맵, 호출부에서 PID 그대로 사용 */ }

        var rootOf = new Dictionary<int, int>();
        foreach (var pid in name.Keys)
        {
            int cur  = pid;
            var seen = new HashSet<int>();   // PID 재사용으로 인한 순환 방지
            while (seen.Add(cur)
                   && parent.TryGetValue(cur, out var par)
                   && name.TryGetValue(par, out var pn)
                   && string.Equals(pn, name[pid], StringComparison.OrdinalIgnoreCase))
            {
                cur = par;
            }
            rootOf[pid] = cur;
        }
        return rootOf;
    }

    private (List<ProcessRow> rows, Dictionary<int, (DateTime, TimeSpan)> cpuMap) BuildProcessRows()
    {
        var now     = DateTime.UtcNow;
        var cpuMap  = new Dictionary<int, (DateTime, TimeSpan)>();
        var rows    = new List<ProcessRow>();
        var privWs  = QueryPrivateWorkingSet();

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

                string? exePath = null;
                try { exePath = p.MainModule?.FileName; } catch { }

                bool hasWindow = false;
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }

                rows.Add(new ProcessRow(
                    Name:        p.ProcessName,
                    Pid:         p.Id,
                    CpuPercent:  Math.Round(cpu, 1),
                    MemoryMB:    Math.Round(
                                     privWs.TryGetValue(p.Id, out var wsp) ? wsp : p.WorkingSet64 / 1_048_576.0, 1),
                    Status:      p.Responding ? "실행 중" : "응답 없음",
                    Description: SafeFileDescription(p),
                    UserName:    "",   // 별도 WMI 조회 생략 (성능)
                    WindowTitle: p.MainWindowTitle,
                    ExePath:     exePath ?? "",
                    HasWindow:   hasWindow));
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
        var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var privWs = QueryPrivateWorkingSet();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var key = p.ProcessName;
                if (!pathMap.ContainsKey(key))
                {
                    try { pathMap[key] = p.MainModule?.FileName ?? ""; } catch { }
                }
                var cpu = p.TotalProcessorTime;
                var mem = privWs.TryGetValue(p.Id, out var wsp) ? wsp : p.WorkingSet64 / 1_048_576.0;
                if (groups.TryGetValue(key, out var g))
                    groups[key] = (g.cpu + cpu, Math.Max(g.mem, mem), g.count + 1);
                else
                    groups[key] = (cpu, mem, 1);
            }
            catch { }
        }
        return groups
            .Select(kv => new AppHistoryRow(
                Name:          kv.Key,
                CpuTimeText:   $"{(int)kv.Value.cpu.TotalHours:D2}:{kv.Value.cpu.Minutes:D2}:{kv.Value.cpu.Seconds:D2}",
                PeakMemoryMB:  Math.Round(kv.Value.mem, 1),
                InstanceCount: kv.Value.count,
                IconSource:    pathMap.TryGetValue(kv.Key, out var p) ? GetIconForPath(p) : null))
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
                    Path:        r.ExePath,
                    IconSource:  GetIconForPath(r.ExePath)))
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
                "SELECT Name,DisplayName,Description,State,StartMode,ProcessId,PathName FROM Win32_Service");
            foreach (ManagementObject obj in searcher.Get())
            {
                var exePath = ParseExePath(obj["PathName"]?.ToString() ?? "");
                rows.Add(new ServiceRow
                {
                    Name        = obj["Name"]?.ToString()        ?? "",
                    DisplayName = obj["DisplayName"]?.ToString() ?? "",
                    Description = obj["Description"]?.ToString() ?? "",
                    State       = obj["State"]?.ToString()       ?? "",
                    StartMode   = obj["StartMode"]?.ToString()   ?? "",
                    Pid         = obj["ProcessId"] is uint pid ? (int)pid : 0,
                    IconSource  = GetIconForPath(exePath)
                });
            }
        }
        catch { }
        return rows;
    }

    private static string ParseExePath(string servicePath)
    {
        if (string.IsNullOrEmpty(servicePath)) return "";
        servicePath = servicePath.Trim();
        if (servicePath.StartsWith('"'))
        {
            var end = servicePath.IndexOf('"', 1);
            if (end > 0) return servicePath[1..end];
        }
        else
        {
            var space = servicePath.IndexOf(' ');
            return space > 0 ? servicePath[..space] : servicePath;
        }
        return servicePath;
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

    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? GetIconForPath(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        return IconCache.GetOrAdd(exePath, path =>
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = ms;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 16;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        });
    }

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
