using System.Management;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WinPilot.ViewModels;

public partial class WindowsSettingsViewModel : ObservableObject
{
    // ─── Delivery Optimization ────────────────────────────────
    // Windows 설정과 동일한 Config 경로만 사용합니다.
    // Policy 경로는 사용하지 않습니다 (Windows 설정 잠금 부작용 발생).
    private const string DoConfigKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    [ObservableProperty] private bool   _deliveryOptEnabled;
    [ObservableProperty] private string _deliveryOptStatus = "로딩 중...";
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
        // ① WinPilot이 이전에 Policy 경로에 쓴 값이 있으면 먼저 정리합니다.
        CleanupWinPilotPolicy();

        // ② 그룹 정책 확인 (IT 관리자가 설정한 경우)
        using var policy = Registry.LocalMachine.OpenSubKey(DoPolicyKey);
        if (policy?.GetValue("DODownloadMode") is int pm)
        {
            DeliveryOptPolicyLocked = true;
            _loading = true;
            DeliveryOptEnabled = pm != 0;
            _loading = false;
            DeliveryOptStatus = $"그룹 정책 적용 중 (모드 {pm}) — IT 관리자 설정, 변경 불가";
            return;
        }

        // ③ Config 경로 읽기 (일반 사용자 설정)
        DeliveryOptPolicyLocked = false;
        using var cfg = Registry.LocalMachine.OpenSubKey(DoConfigKey);
        int mode = cfg?.GetValue("DODownloadMode") is int m ? m : 1;
        _loading = true;
        DeliveryOptEnabled = mode != 0;
        _loading = false;
        DeliveryOptStatus = DescribeMode(mode);
    }

    /// WinPilot이 이전 버전에서 Policy 경로에 남긴 값을 정리합니다.
    private static void CleanupWinPilotPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DoPolicyKey, writable: true);
            if (key == null) return;

            var values = key.GetValueNames();
            // WinPilotManaged 마커가 있거나, DODownloadMode만 단독 존재 → WinPilot이 쓴 것
            bool hasMarker = values.Contains("WinPilotManaged");
            bool onlyDoMode = values.Length == 1 && values[0] == "DODownloadMode"
                              && key.GetSubKeyNames().Length == 0;

            if (hasMarker || onlyDoMode)
            {
                key.DeleteValue("DODownloadMode",    throwOnMissingValue: false);
                key.DeleteValue("WinPilotManaged",   throwOnMissingValue: false);

                // 키가 완전히 비었으면 삭제
                if (key.GetValueNames().Length == 0 && key.GetSubKeyNames().Length == 0)
                    Registry.LocalMachine.DeleteSubKey(DoPolicyKey, throwOnMissingSubKey: false);
            }
        }
        catch { /* 권한 없으면 무시 */ }
    }

    private void ApplyDeliveryOpt(bool enable)
    {
        try
        {
            int mode = enable ? 1 : 0;

            // Config 경로에만 씁니다.
            // Windows 설정도 이 경로를 읽으므로 페이지 재진입 시 반영됩니다.
            using var key = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            key.SetValue("DODownloadMode", mode, RegistryValueKind.DWord);

            // MDM Bridge 추가 시도 (지원 PC에서 즉시 반영)
            TrySetViaMdmBridge(mode);

            LoadDeliveryOpt();
        }
        catch (Exception ex)
        {
            _loading = true;
            DeliveryOptEnabled = !enable;
            _loading = false;
            LoadDeliveryOpt();
            MessageBox.Show($"설정 변경 실패:\n{ex.Message}", "WinPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void TrySetViaMdmBridge(int mode)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2\mdm\dmmap");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery(
                    "SELECT * FROM MDM_Policy_Config01_DeliveryOptimization02 " +
                    "WHERE InstanceID='DeliveryOptimization' " +
                    "AND ParentID='./Vendor/MSFT/Policy/Config'"));

            ManagementObject? existing = null;
            foreach (ManagementObject o in searcher.Get()) { existing = o; break; }

            if (existing != null)
            {
                existing["DODownloadMode"] = (uint)mode;
                existing.Put();
            }
            else
            {
                using var cls = new ManagementClass(scope,
                    new ManagementPath("MDM_Policy_Config01_DeliveryOptimization02"), null);
                using var inst = cls.CreateInstance();
                inst["InstanceID"] = "DeliveryOptimization";
                inst["ParentID"]   = "./Vendor/MSFT/Policy/Config";
                inst["DODownloadMode"] = (uint)mode;
                inst.Put();
            }
        }
        catch { /* MDM Bridge 미지원이면 무시 */ }
    }

    private static string DescribeMode(int mode) => mode switch
    {
        0 => "꺼짐  (HTTP만 사용)",
        1 => "켜짐  — 로컬 네트워크만",
        2 => "켜짐  — 인터넷 + 로컬 네트워크",
        3 => "켜짐  — 그룹 (LAN + 인터넷)",
        _ => $"켜짐  (모드 {mode})"
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
    private void Refresh() => RefreshAll();

    private void RefreshAll()
    {
        LoadDeliveryOpt();
        LoadSac();
    }
}
