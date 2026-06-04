using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public enum StepStatus { Waiting, Running, Completed, Failed }

public partial class RecoveryStep : ObservableObject
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string CommandDisplay => $"{FileName} {Arguments}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private StepStatus _status = StepStatus.Waiting;

    public string StatusText => Status switch
    {
        StepStatus.Running   => "실행 중...",
        StepStatus.Completed => "완료",
        StepStatus.Failed    => "실패",
        _ => "대기"
    };
}

public partial class RecoveryViewModel : ObservableObject
{
    private readonly RecoveryService _service = new();
    // 진행률 줄 패턴
    // DISM: "[====55.5%====]"
    // SFC:  "          1"  또는 "          1%"  (앞에 공백이 많은 숫자)
    private static readonly Regex ProgressLineRx = new(
        @"(^\[=+\s*\d+\.?\d*\s*%)|(^\s{2,}\d+\.?\d*\s*%?\s*$)",
        RegexOptions.Compiled);

    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isRunning;
    public bool IsNotAdmin => !IsAdmin;
    [ObservableProperty] private bool _isProgressUpdate;
    [ObservableProperty] private string _currentOutput = "실행 버튼을 눌러 복구를 시작합니다.";
    [ObservableProperty] private ObservableCollection<RecoveryStep> _steps;

    public RecoveryViewModel()
    {
        IsAdmin = RecoveryService.IsRunningAsAdmin();
        _steps = new ObservableCollection<RecoveryStep>(CreateSteps());
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunAllStepsCommand.NotifyCanExecuteChanged();
        RunStepCommand.NotifyCanExecuteChanged();
    }

    private static List<RecoveryStep> CreateSteps() =>
    [
        new() { Number = 1, Title = "DISM CheckHealth",   FileName = "dism", Arguments = "/Online /Cleanup-Image /CheckHealth" },
        new() { Number = 2, Title = "DISM ScanHealth",    FileName = "dism", Arguments = "/Online /Cleanup-Image /ScanHealth" },
        new() { Number = 3, Title = "DISM RestoreHealth", FileName = "dism", Arguments = "/Online /Cleanup-Image /RestoreHealth" },
        new() { Number = 4, Title = "SFC /scannow",       FileName = "sfc",  Arguments = "/scannow" },
    ];

    private bool CanRun() => !IsRunning;

    [RelayCommand]
    private void RestartAsAdmin() => RecoveryService.RestartAsAdmin();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAllStepsAsync()
    {
        if (!RequireAdmin()) return;

        IsRunning = true;
        Steps = new ObservableCollection<RecoveryStep>(CreateSteps());
        var outputAll = new StringBuilder();

        foreach (var step in Steps)
            await ExecuteStepAsync(step, outputAll);

        IsRunning = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunStepAsync(RecoveryStep? step)
    {
        if (step == null || !RequireAdmin()) return;

        IsRunning = true;
        step.Status = StepStatus.Waiting;
        var outputBuffer = new StringBuilder();
        await ExecuteStepAsync(step, outputBuffer);
        IsRunning = false;
    }

    private async Task ExecuteStepAsync(RecoveryStep step, StringBuilder outputAll)
    {
        step.Status = StepStatus.Running;
        outputAll.AppendLine($"{'=',50}");
        outputAll.AppendLine($"[단계 {step.Number}] {step.Title}");
        outputAll.AppendLine($"명령: {step.CommandDisplay}");
        outputAll.AppendLine($"{'=',50}");
        CurrentOutput = outputAll.ToString();

        var tcs = new TaskCompletionSource<int>();
        var stepLines = new List<string>();
        int lastProgressIdx = -1;
        var outputLock = new object(); // stdout + stderr 핸들러가 별도 스레드에서 동시에 호출

        var process = _service.StartCommand(
            step.FileName, step.Arguments,
            (_, e) =>
            {
                if (e.Data == null) return;
                string snapshot;
                bool wasProgressUpdate;
                lock (outputLock)
                {
                    bool isProgress = ProgressLineRx.IsMatch(e.Data.TrimStart());
                    wasProgressUpdate = isProgress && lastProgressIdx >= 0;
                    if (wasProgressUpdate)
                        stepLines[lastProgressIdx] = e.Data;
                    else
                    {
                        stepLines.Add(e.Data);
                        if (isProgress) lastProgressIdx = stepLines.Count - 1;
                    }
                    snapshot = string.Join(Environment.NewLine, stepLines);
                }
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsProgressUpdate = wasProgressUpdate;
                    CurrentOutput = outputAll + snapshot;
                });
            },
            (sender, _) =>
            {
                var p = sender as System.Diagnostics.Process;
                tcs.TrySetResult(p?.ExitCode ?? -1);
            });

        if (process == null)
        {
            step.Status = StepStatus.Failed;
            outputAll.AppendLine("[오류] 프로세스를 시작할 수 없습니다.\n");
            CurrentOutput = outputAll.ToString();
            return;
        }

        int exitCode = await tcs.Task;
        step.Status = exitCode == 0 ? StepStatus.Completed : StepStatus.Failed;
        string finalOutput;
        lock (outputLock) { finalOutput = string.Join(Environment.NewLine, stepLines); }
        if (finalOutput.Length > 0)
            outputAll.AppendLine(finalOutput);
        outputAll.AppendLine($"\n→ 종료 코드: {exitCode}  ({step.StatusText})\n");
        CurrentOutput = outputAll.ToString();
    }

    private bool RequireAdmin()
    {
        if (IsAdmin) return true;
        MessageBox.Show("복구 도구는 관리자 권한이 필요합니다.\n'관리자로 재시작' 버튼을 클릭하세요.",
            "WinPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
