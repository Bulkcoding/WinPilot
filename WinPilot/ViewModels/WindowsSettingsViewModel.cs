using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WinPilot.ViewModels;

public partial class WindowsSettingsViewModel : ObservableObject
{
    // ─── Delivery Optimization ────────────────────────────────
    private const string DoConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    [ObservableProperty] private bool _deliveryOptEnabled;
    [ObservableProperty] private string _deliveryOptStatus = "";
    [ObservableProperty] private bool _deliveryOptPolicyLocked;

    // ─── Smart App Control ────────────────────────────────────
    private const string SacKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";

    [ObservableProperty] private int _sacState = -1;   // -1=미지원, 0=Off, 1=평가, 2=On
    [ObservableProperty] private string _sacStatusText = "";
    [ObservableProperty] private bool _sacSupported;

    private bool _loading;

    public WindowsSettingsViewModel() => Refresh();

    // ─── Delivery Optimization ────────────────────────────────

    partial void OnDeliveryOptEnabledChanged(bool value)
    {
        if (_loading) return;
        ApplyDeliveryOpt(value);
    }

    private void LoadDeliveryOpt()
    {
        try
        {
            // 그룹 정책이 있으면 우선
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
            using var cfg = Registry.LocalMachine.OpenSubKey(DoConfigKey);
            // 기본값은 1(LAN peers 허용)
            int mode = cfg?.GetValue("DODownloadMode") is int m ? m : 1;

            _loading = true;
            DeliveryOptEnabled = mode != 0;
            _loading = false;

            DeliveryOptStatus = mode switch
            {
                0 => "꺼짐 — HTTP만 사용 (피어 다운로드 없음)",
                1 => "켜짐 — 로컬 네트워크만",
                2 => "켜짐 — 인터넷 + 로컬 네트워크",
                3 => "켜짐 — 그룹 (HTTP + LAN + 인터넷)",
                _ => $"켜짐 (모드 {mode})"
            };
        }
        catch (Exception ex)
        {
            DeliveryOptStatus = $"읽기 실패: {ex.Message}";
        }
    }

    private void ApplyDeliveryOpt(bool enable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            // 켜기 → 1(로컬 네트워크 피어), 끄기 → 0
            key.SetValue("DODownloadMode", enable ? 1 : 0, RegistryValueKind.DWord);
            LoadDeliveryOpt();
        }
        catch (Exception ex)
        {
            // 실패 시 원상복구
            _loading = true;
            DeliveryOptEnabled = !enable;
            _loading = false;
            MessageBox.Show($"배달 최적화 설정 변경 실패:\n{ex.Message}", "WinPilot",
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
        if (!int.TryParse(stateStr, out int newState)) return;
        if (!SacSupported) return;

        // 끄기 전 경고
        if (newState == 0 && SacState != 0)
        {
            var res = MessageBox.Show(
                "스마트 앱 컨트롤을 끄면 Windows 보안 설정에서 다시 켤 수 없습니다.\n" +
                "(레지스트리로만 복원 가능, 재부팅 필요)\n\n계속하시겠습니까?",
                "WinPilot — 주의", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
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
            MessageBox.Show($"스마트 앱 컨트롤 변경 실패:\n{ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadDeliveryOpt();
        LoadSac();
    }
}
