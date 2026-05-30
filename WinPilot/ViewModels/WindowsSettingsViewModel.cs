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

            // ① MDM Bridge WMI 시도 (성공 시 DoSvc에 즉시 반영됨)
            bool mdmOk = TrySetViaMdmBridge(mode);

            // ② 레지스트리에도 씁니다 (재부팅 후에도 유지)
            using var key = Registry.LocalMachine.CreateSubKey(DoConfigKey, writable: true);
            key.SetValue("DODownloadMode", mode, RegistryValueKind.DWord);

            LoadDeliveryOpt();

            if (!mdmOk)
                DeliveryOptStatus += "  ※ 서비스 즉시 반영 실패 — Windows 설정을 닫았다 다시 열어 확인하세요";
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
    /// MDM Bridge WMI를 통해 DoSvc에 직접 설정을 전달합니다.
    /// Windows 설정 앱이 내부적으로 사용하는 경로와 동일한 방식으로
    /// CSP(Configuration Service Provider)를 통해 서비스에 반영됩니다.
    ///
    /// 경로: root\cimv2\mdm\dmmap → MDM_Policy_Config01_DeliveryOptimization02
    /// CSP: ./Vendor/MSFT/Policy/Config/DeliveryOptimization/DODownloadMode
    /// </summary>
    private static bool TrySetViaMdmBridge(int mode)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2\mdm\dmmap");
            scope.Connect();

            // 기존 인스턴스 조회
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
                // 기존 인스턴스 업데이트
                existing["DODownloadMode"] = (uint)mode;
                existing.Put();
            }
            else
            {
                // 새 인스턴스 생성
                using var cls = new ManagementClass(scope,
                    new ManagementPath("MDM_Policy_Config01_DeliveryOptimization02"), null);
                using var inst = cls.CreateInstance();
                inst["InstanceID"] = "DeliveryOptimization";
                inst["ParentID"]   = "./Vendor/MSFT/Policy/Config";
                inst["DODownloadMode"] = (uint)mode;
                inst.Put();
            }

            return true;
        }
        catch
        {
            // MDM Bridge 사용 불가 (미지원 PC, 권한 부족 등)
            return false;
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
