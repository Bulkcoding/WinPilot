using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Models;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class EventViewerViewModel : ObservableObject
{
    private readonly EventLogService _service = new();
    private List<LogEntry> _filteredEntries = [];

    [ObservableProperty] private ObservableCollection<LogEntry> _entries = [];
    [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-1);
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private string _selectedLogFilter = "모든 로그";
    [ObservableProperty] private string _selectedLevelFilter = "경고, 오류";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private LogEntry? _selectedEntry;
    [ObservableProperty] private bool _isDetailVisible;

    private const int PageSize = 12;

    public IList<string> LogFilters { get; } = ["모든 로그", "System", "Application"];
    public IList<string> LevelFilters { get; } = ["모든 수준", "경고, 오류", "오류만"];

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        var all = await Task.Run(() => _service.GetEntries(FromDate, ToDate.AddDays(1)));
        ApplyFilter(all);
        IsLoading = false;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) { CurrentPage--; UpdatePage(); }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) { CurrentPage++; UpdatePage(); }
    }

    [RelayCommand]
    private void CloseDetail() => IsDetailVisible = false;

    partial void OnSelectedLogFilterChanged(string value) { _ = RefreshAsync(); }
    partial void OnSelectedLevelFilterChanged(string value) { _ = RefreshAsync(); }

    private void ApplyFilter(List<LogEntry> all)
    {
        var filtered = all.AsEnumerable();

        if (SelectedLogFilter != "모든 로그")
            filtered = filtered.Where(e => e.LogName == SelectedLogFilter);

        _filteredEntries = SelectedLevelFilter switch
        {
            "오류만" => filtered.Where(e => e.Level == LogLevel.Error).ToList(),
            "경고, 오류" => filtered.Where(e => e.Level is LogLevel.Error or LogLevel.Warning).ToList(),
            _ => filtered.ToList()
        };

        TotalCount = _filteredEntries.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(_filteredEntries.Count / (double)PageSize));
        CurrentPage = 1;
        UpdatePage();
    }

    private void UpdatePage()
    {
        var page = _filteredEntries.Skip((CurrentPage - 1) * PageSize).Take(PageSize);
        Entries = new ObservableCollection<LogEntry>(page);
    }
}
