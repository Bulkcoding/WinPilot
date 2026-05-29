using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WinPilot.ViewModels;

public partial class WindowsSettingsViewModel : ObservableObject
{
    // ─── Delivery Optimization ────────────────────────────────
    // Windows 설정이 읽고 쓰는 실제 경로
    private const string DoConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    [ObservableProperty] private bool   _deliveryOptEnabled;
    [ObservableProperty] private string _deliveryOptStatus  = "로딩 중...";
    [ObservableProperty] private bool   _deliveryOptPolicyLocked;

    // ─── Smart App Control ────────────────────────────────────
    private const string SacKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    [ObservableProperty] private int    _sacState = -1;
    [ObservableProperty] private string _sacStatusText = "";
    [ObservableProperty] private bool   _sacSupported;

    private bool _loading;

    public WindowsSettingsViewModel() => RefreshAll();

    // ─── Delivery Optimization ────────────────────────────────

    partial void OnDeliveryOptEnabledChanged(bool value)
    {
        if (_loading) return;
        ApplyDeliveryOpt(value);
    }

    private void LoadDeliveryOpt()
    {
        // ① 그룹 정책 확인 (최우선, 쓰기 불가)
        using var policy = Registry.LocalMachine.OpenSubKey(DoPolicyKey);
        if (policy?.GetValue("DODownloadMode") is int pm)
        {
            DeliveryOptPolicyLocked = true;
            _loading = true;
            DeliveryOptEnabled = pm != 0;
            _loading = false;
            DeliveryOptStatus = $"그룹 정책 적용 중 (모드 {pm}) — 변경 불가";
            return;
        }

        DeliveryOptPolicyLocked = false;

        // ② Config 레지스트리 읽기
        // Windows 설정도 이 경로를 읽고 씁니다.
        // 값이 없으면 기본값 = 1 (LAN 피어 허용)
        using var cfg = Registry.LocalMachine.OpenSubKey(DoConfigKey);
        int mode = cfg?.GetValue("DODownloadMode") is int m ? m : 1;

        _loading = true;
        DeliveryOptEnabled = mode != 0;
        _loading = false;

        DeliveryOptStatus = mode switch
        {
            0 => "꺼짐  (HTTP만 사용, 피어 다운로드 없음)",
            1 => "켜짐  — 로컬 네트워크만",
            2 => "켜짐  — 인터넷 + 로컬 네트워크",
            3 => "켜짐  — 그룹 (LAN + 인터넷)",
            _ => $"켜짐  (모드 {mode})"
        };
    }

    private void ApplyDeliveryOpt(bool enable)
    {
        try
        {
            int mode = enable ? 1 : 0;

            // Config 경로에 씁니다.
            // Windows 설정과 동일한 경로를 사용합니다.
            using var key = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            key.SetValue("DODownloadMode", mode, RegistryValueKind.DWord);

            // 즉시 다시 읽어서 UI 갱신
            LoadDeliveryOpt();

            // ⚠ DoSvc(배달 최적화 서비스)는 OS 보호 서비스라 외부에서
            //   재시작이 불가능합니다. 설정은 레지스트리에 저장되며
            //   Windows 설정 앱을 닫았다 다시 열면 반영됩니다.
            //   실제 다운로드 동작은 다음 Windows Update 시점에 적용됩니다.
        }
        catch (Exception ex)
        {
            // 실패 시 UI 롤백
            _loading = true;
            DeliveryOptEnabled = !enable;
            _loading = false;
            LoadDeliveryOpt();
            MessageBox.Show($"설정 변경 실패:\n{ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
    private void Refresh() => RefreshAll();

    private void RefreshAll()
    {
        LoadDeliveryOpt();
        LoadSac();
    }
}
