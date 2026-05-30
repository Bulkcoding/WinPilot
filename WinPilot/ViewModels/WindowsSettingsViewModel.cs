using System.Diagnostics;
using System.Management;
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
            var log = new System.Text.StringBuilder();

            // ① MDM Bridge WMI (Windows 설정 앱과 동일한 CSP 경로)
            string? mdmErr = TrySetViaMdmBridge(mode);
            log.AppendLine(mdmErr == null ? "MDM Bridge: 성공" : $"MDM Bridge: 실패 ({mdmErr})");

            // ② Policy 레지스트리 (DoSvc가 동적으로 인식, WinPilot 고권한 필요)
            string? policyErr = TrySetPolicyRegistry(mode);
            log.AppendLine(policyErr == null ? "Policy 레지스트리: 성공" : $"Policy 레지스트리: 실패 ({policyErr})");

            // ③ Config 레지스트리 (재부팅 후 적용 보장)
            using var cfgKey = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            cfgKey.SetValue("DODownloadMode", mode, RegistryValueKind.DWord);
            log.AppendLine("Config 레지스트리: 성공");

            LoadDeliveryOpt();

            // 적어도 하나 이상 성공했는지 확인
            bool anyLive = mdmErr == null || policyErr == null;
            if (!anyLive)
            {
                DeliveryOptStatus += "  ※ 즉시 반영 불가 — Windows 설정 페이지 닫고 다시 열어 확인";
                // 실패 원인 표시 (진단용)
                MessageBox.Show(log.ToString(), "WinPilot — 배달 최적화 진단",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
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

    /// Policy 경로에 쓰기 — DoSvc가 이 경로를 우선 읽으며 동적 반영됩니다.
    /// WinPilot은 High Integrity 관리자로 실행되므로 가능할 수 있습니다.
    /// 성공 시 null, 실패 시 오류 메시지 반환.
    private static string? TrySetPolicyRegistry(int mode)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(DoPolicyKey, writable: true);
            if (mode == 0)
                key.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
            else
            {
                // ON이면 Policy 강제 설정을 제거해 Config 기본값으로 돌아가게 함
                try { key.DeleteValue("DODownloadMode", throwOnMissingValue: false); } catch { }
                // Policy 키 자체를 삭제하려 시도
                try { Registry.LocalMachine.DeleteSubKey(DoPolicyKey, throwOnMissingSubKey: false); } catch { }
            }
            return null; // 성공
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// MDM Bridge WMI로 DoSvc에 직접 설정 전달.
    /// 성공 시 null, 실패 시 오류 메시지 반환.
    private static string? TrySetViaMdmBridge(int mode)
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
            foreach (ManagementObject o in searcher.Get())
            { existing = o; break; }

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

            return null; // 성공
        }
        catch (Exception ex)
        {
            return ex.Message;
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
