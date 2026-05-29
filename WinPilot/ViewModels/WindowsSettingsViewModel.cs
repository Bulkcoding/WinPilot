using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WinPilot.ViewModels;

public partial class WindowsSettingsViewModel : ObservableObject
{
    // ─── Delivery Optimization ────────────────────────────────
    // Windows 설정 앱이 읽고 쓰는 경로 (Win10/11 공통)
    private const string DoConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    [ObservableProperty] private bool   _deliveryOptEnabled;
    [ObservableProperty] private string _deliveryOptStatus  = "로딩 중...";
    [ObservableProperty] private bool   _deliveryOptPolicyLocked;
    [ObservableProperty] private bool   _deliveryOptApplying;

    // ─── Smart App Control ────────────────────────────────────
    private const string SacKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    [ObservableProperty] private int    _sacState = -1;
    [ObservableProperty] private string _sacStatusText = "";
    [ObservableProperty] private bool   _sacSupported;

    private bool _loading;

    public WindowsSettingsViewModel() => _ = RefreshAllAsync();

    // ─── Delivery Optimization ────────────────────────────────

    partial void OnDeliveryOptEnabledChanged(bool value)
    {
        if (_loading) return;
        _ = ApplyDeliveryOptAsync(value);
    }

    /// <summary>
    /// PowerShell Get-DeliveryOptimizationStatus로 라이브 상태를 읽습니다.
    /// Windows 설정 앱과 동일한 값을 보여줍니다.
    /// </summary>
    private async Task LoadDeliveryOptAsync()
    {
        try
        {
            // 1) 그룹 정책 확인 (최우선)
            using var policy = Registry.LocalMachine.OpenSubKey(DoPolicyKey);
            if (policy?.GetValue("DODownloadMode") is int policyMode)
            {
                DeliveryOptPolicyLocked = true;
                _loading = true;
                DeliveryOptEnabled = policyMode != 0;
                _loading = false;
                DeliveryOptStatus = $"그룹 정책 적용 중 (모드 {policyMode}) — 여기서 변경 불가";
                return;
            }

            DeliveryOptPolicyLocked = false;

            // 2) PowerShell로 라이브 DO 모드 읽기 (Windows 설정과 동일한 소스)
            int mode = await Task.Run(GetLiveDoMode);

            _loading = true;
            DeliveryOptEnabled = mode != 0;
            _loading = false;
            DeliveryOptStatus = DescribeMode(mode);
        }
        catch (Exception ex)
        {
            DeliveryOptStatus = $"읽기 실패: {ex.Message}";
        }
    }

    /// <summary>
    /// PowerShell Get-DeliveryOptimizationStatus로 현재 실제 모드를 반환합니다.
    /// 실패 시 레지스트리 직접 읽기로 폴백합니다.
    /// </summary>
    private static int GetLiveDoMode()
    {
        // ① PowerShell로 서비스의 라이브 상태 확인
        try
        {
            var raw = RunPs("(Get-DeliveryOptimizationStatus).DownloadMode");
            if (int.TryParse(raw.Trim(), out int liveMode))
                return liveMode;
        }
        catch { }

        // ② 폴백: 레지스트리 직접 읽기
        try
        {
            using var cfg = Registry.LocalMachine.OpenSubKey(DoConfigKey);
            return cfg?.GetValue("DODownloadMode") is int m ? m : 1;
        }
        catch { return 1; }
    }

    private async Task ApplyDeliveryOptAsync(bool enable)
    {
        try
        {
            DeliveryOptApplying = true;
            DeliveryOptStatus = "레지스트리 변경 중...";

            // 1) 레지스트리에 쓰기
            using (var key = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true))
                key.SetValue("DODownloadMode", enable ? 1 : 0, RegistryValueKind.DWord);

            DeliveryOptStatus = "서비스 재시작 중... (최대 15초)";

            // 2) DoSvc 재시작 (없으면 Windows 설정에 즉시 반영 안 됨)
            bool restarted = await Task.Run(RestartDoSvc);

            DeliveryOptApplying = false;

            // 3) 재시작 후 라이브 상태 다시 읽기
            await LoadDeliveryOptAsync();

            if (!restarted)
                DeliveryOptStatus += "  ※ 서비스 재시작 실패 — 재부팅 후 적용됩니다";
        }
        catch (Exception ex)
        {
            DeliveryOptApplying = false;
            _loading = true;
            DeliveryOptEnabled = !enable;   // UI 롤백
            _loading = false;
            MessageBox.Show($"설정 변경 실패:\n{ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
            await LoadDeliveryOptAsync();
        }
    }

    /// <summary>
    /// PowerShell Restart-Service 우선, 실패 시 sc.exe 폴백.
    /// true = 재시작 성공.
    /// </summary>
    private static bool RestartDoSvc()
    {
        // ① PowerShell Restart-Service (가장 안정적)
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -WindowStyle Hidden " +
                "-Command \"Restart-Service -Name DoSvc -Force -ErrorAction Stop\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            if (p?.ExitCode == 0) return true;
        }
        catch { }

        // ② sc.exe 폴백
        try
        {
            using var stop = Process.Start(new ProcessStartInfo("sc.exe", "stop DoSvc")
                { UseShellExecute = false, CreateNoWindow = true });
            stop?.WaitForExit(8000);

            Thread.Sleep(2000);

            using var start = Process.Start(new ProcessStartInfo("sc.exe", "start DoSvc")
                { UseShellExecute = false, CreateNoWindow = true });
            start?.WaitForExit(8000);
            return start?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string DescribeMode(int mode) => mode switch
    {
        0 => "꺼짐 — HTTP만 사용 (피어 다운로드 없음)",
        1 => "켜짐 — 로컬 네트워크만",
        2 => "켜짐 — 인터넷 + 로컬 네트워크",
        3 => "켜짐 — 그룹 (HTTP + LAN + 인터넷)",
        _ => $"켜짐 (모드 {mode})"
    };

    // ─── Smart App Control ────────────────────────────────────

    private void LoadSac()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SacKey);
            if (key?.GetValue("VerifiedAndReputablePolicyState") is int val)
            {
                SacSupported = true;
                SacState = val;
                SacStatusText = val switch
                {
                    0 => "꺼짐",
                    1 => "평가 모드 (자동 판단)",
                    2 => "켜짐",
                    _ => $"알 수 없음 ({val})"
                };
            }
            else
            {
                SacSupported = false;
                SacState = -1;
                SacStatusText = "이 PC는 스마트 앱 컨트롤을 지원하지 않습니다 (Windows 11 22H2+ 필요)";
            }
        }
        catch
        {
            SacSupported = false;
            SacStatusText = "레지스트리 읽기 실패";
        }
    }

    [RelayCommand]
    private void SetSac(string? stateStr)
    {
        if (!int.TryParse(stateStr, out int newState) || !SacSupported) return;

        if (newState == 0 && SacState != 0)
        {
            var r = MessageBox.Show(
                "스마트 앱 컨트롤을 끄면 Windows 보안 설정에서 다시 켤 수 없습니다.\n" +
                "(레지스트리 수정으로만 복원 가능, 재부팅 필요)\n\n계속하시겠습니까?",
                "WinPilot — 주의", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SacKey, writable: true)
                ?? throw new InvalidOperationException("레지스트리 키를 열 수 없습니다.");
            key.SetValue("VerifiedAndReputablePolicyState", newState, RegistryValueKind.DWord);
            MessageBox.Show("설정이 변경되었습니다. 재부팅 후 적용됩니다.",
                "WinPilot", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadSac();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"변경 실패:\n{ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Refresh() => _ = RefreshAllAsync();

    private async Task RefreshAllAsync()
    {
        await LoadDeliveryOptAsync();
        LoadSac();
    }

    // ─── PowerShell 헬퍼 ───────────────────────────────────────

    private static string RunPs(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new Exception("PowerShell 실행 실패");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(8000);
        return output.Trim();
    }
}
