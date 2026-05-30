using System.Management;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WinPilot.ViewModels;

public partial class WindowsSettingsViewModel : ObservableObject
{
    // ─── Delivery Optimization ────────────────────────────────
    private const string DoConfigKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyKey  = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    // WinPilot이 Policy 키를 직접 썼음을 표시하는 마커 값 이름
    private const string WinPilotMarker = "WinPilotManaged";

    [ObservableProperty] private bool   _deliveryOptEnabled;
    [ObservableProperty] private string _deliveryOptStatus = "로딩 중...";
    [ObservableProperty] private bool   _deliveryOptPolicyLocked;  // 실제 IT 정책만 잠금

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
        using var policy = Registry.LocalMachine.OpenSubKey(DoPolicyKey);
        if (policy?.GetValue("DODownloadMode") is int pm)
        {
            bool winPilotManaged = IsWinPilotPolicy(policy);

            if (!winPilotManaged)
            {
                // ① 진짜 IT/그룹 정책 (DODownloadMode 외 다른 값이 있음) → 잠금
                DeliveryOptPolicyLocked = true;
                _loading = true;
                DeliveryOptEnabled = pm != 0;
                _loading = false;
                DeliveryOptStatus = $"그룹 정책 적용 중 (모드 {pm}) — IT 관리자 설정, 변경 불가";
                return;
            }

            // ② WinPilot이 설정한 Policy → 잠금 해제, 변경 가능
            // (마커가 있거나, DODownloadMode 단독 존재 = 이전 버전 WinPilot이 설정)
            DeliveryOptPolicyLocked = false;
            _loading = true;
            DeliveryOptEnabled = pm != 0;
            _loading = false;
            DeliveryOptStatus = pm switch
            {
                0 => "꺼짐  (즉시 반영됨)",
                1 => "켜짐  — 로컬 네트워크만",
                _ => $"켜짐  (모드 {pm})"
            };
            return;
        }

        // ③ 정책 없음 → Config 경로 읽기
        DeliveryOptPolicyLocked = false;
        using var cfg = Registry.LocalMachine.OpenSubKey(DoConfigKey);
        int mode = cfg?.GetValue("DODownloadMode") is int m ? m : 1;
        _loading = true;
        DeliveryOptEnabled = mode != 0;
        _loading = false;
        DeliveryOptStatus = DescribeMode(mode);
    }

    /// WinPilot이 관리하는 Policy 키인지 판별합니다.
    /// 마커가 있거나, DODownloadMode 값만 단독으로 존재하면 WinPilot이 만든 것.
    /// IT 관리자 정책은 보통 다른 DO 값(DOCacheHost, DOGroupId 등)도 함께 있습니다.
    private static bool IsWinPilotPolicy(RegistryKey key)
    {
        if (key.GetValue(WinPilotMarker) != null) return true;  // 신규 마커 있음

        // 구버전 호환: 값이 DODownloadMode 하나뿐이면 WinPilot이 쓴 것으로 간주
        var values = key.GetValueNames();
        return values.Length == 1 && values[0] == "DODownloadMode"
               && key.GetSubKeyNames().Length == 0;
    }

    private void ApplyDeliveryOpt(bool enable)
    {
        try
        {
            int mode = enable ? 1 : 0;

            // ① Policy 경로 업데이트 (DoSvc 즉시 반영)
            ApplyPolicyRegistry(mode);

            // ② MDM Bridge 시도 (추가 반영 경로)
            TrySetViaMdmBridge(mode);

            // ③ Config 경로도 유지 (재부팅 후 보장)
            using var cfgKey = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            cfgKey.SetValue("DODownloadMode", mode, RegistryValueKind.DWord);

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

    /// <summary>
    /// Policy 레지스트리에 WinPilotManaged 마커와 함께 DO 모드를 씁니다.
    /// DoSvc는 Policy 경로를 우선 읽으므로 서비스 재시작 없이 즉시 반영됩니다.
    /// OFF(0): Policy 키에 값을 씁니다.
    /// ON(1):  WinPilot이 쓴 Policy 값을 삭제해 Config 경로로 돌아갑니다.
    /// </summary>
    private static void ApplyPolicyRegistry(int mode)
    {
        if (mode == 0)
        {
            // 끄기: Policy 키에 DODownloadMode=0 + WinPilotManaged 마커 쓰기
            using var key = Registry.LocalMachine.CreateSubKey(DoPolicyKey, writable: true);
            key.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
            key.SetValue(WinPilotMarker, 1, RegistryValueKind.DWord);
        }
        else
        {
            // 켜기: WinPilot이 쓴 Policy 값 제거 → Config 경로로 폴백
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(DoPolicyKey, writable: true);
                if (key == null) return;

                // WinPilot 마커가 있을 때만 제거 (실제 IT 정책은 건드리지 않음)
                if (key.GetValue(WinPilotMarker) != null)
                {
                    key.DeleteValue("DODownloadMode", throwOnMissingValue: false);
                    key.DeleteValue(WinPilotMarker, throwOnMissingValue: false);

                    // 키가 완전히 비었으면 삭제
                    if (key.GetValueNames().Length == 0 && key.GetSubKeyNames().Length == 0)
                        Registry.LocalMachine.DeleteSubKey(DoPolicyKey, throwOnMissingSubKey: false);
                }
            }
            catch { /* 실제 IT 정책이면 무시 */ }
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
        catch { /* MDM Bridge 미지원 PC이면 무시 */ }
    }

    private static string DescribeMode(int mode) => mode switch
    {
        0 => "꺼짐  (HTTP만 사용, 피어 다운로드 없음)",
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
