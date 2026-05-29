using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

public partial class PingResultItem : ObservableObject
{
    public int Seq { get; init; }
    public string Host { get; init; } = "";
    public string RttText { get; init; } = "";
    public string TtlText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public bool IsSuccess { get; init; }
}

public partial class PingViewModel : ObservableObject
{
    [ObservableProperty] private string _host = "8.8.8.8";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _summary = "Ping 버튼을 눌러 시작하세요.";

    public ObservableCollection<PingResultItem> Results { get; } = [];

    private CancellationTokenSource? _cts;
    private int _seq;
    private int _successCount;

    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(Host);
    private bool CanStop() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        PingOnceCommand.NotifyCanExecuteChanged();
        StartContinuousCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    // 단일 Ping
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PingOnceAsync()
    {
        IsRunning = true;
        _seq = 0;
        _successCount = 0;
        Results.Clear();
        await SendPingAsync(Host.Trim());
        IsRunning = false;
    }

    // 연속 Ping (1초 간격)
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task StartContinuousAsync()
    {
        _seq = 0;
        _successCount = 0;
        Results.Clear();
        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await SendPingAsync(Host.Trim());
                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    private async Task SendPingAsync(string host)
    {
        _seq++;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            bool ok = reply.Status == IPStatus.Success;
            if (ok) _successCount++;

            Results.Insert(0, new PingResultItem
            {
                Seq = _seq,
                Host = host,
                RttText = ok ? $"{reply.RoundtripTime} ms" : "-",
                TtlText = ok ? (reply.Options?.Ttl.ToString() ?? "-") : "-",
                StatusText = ok ? "성공" : MapStatus(reply.Status),
                IsSuccess = ok
            });
        }
        catch (Exception ex)
        {
            Results.Insert(0, new PingResultItem
            {
                Seq = _seq, Host = host,
                RttText = "-", TtlText = "-",
                StatusText = $"오류: {ex.Message}",
                IsSuccess = false
            });
        }

        // 최대 200개 유지
        while (Results.Count > 200) Results.RemoveAt(Results.Count - 1);
        UpdateSummary();
    }

    private static string MapStatus(IPStatus status) => status switch
    {
        IPStatus.TimedOut         => "시간 초과",
        IPStatus.DestinationHostUnreachable => "호스트 도달 불가",
        IPStatus.DestinationNetworkUnreachable => "네트워크 도달 불가",
        IPStatus.TtlExpired       => "TTL 만료",
        _                         => status.ToString()
    };

    private void UpdateSummary()
    {
        if (_seq == 0) return;
        int lost = _seq - _successCount;
        double lossRate = lost * 100.0 / _seq;
        Summary = $"전송: {_seq}  |  수신: {_successCount}  |  손실: {lost} ({lossRate:F0}%)";
    }
}
