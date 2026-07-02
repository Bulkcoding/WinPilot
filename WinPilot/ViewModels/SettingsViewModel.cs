using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Models;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public static SettingsViewModel Current { get; } = new();

    private static readonly string _deepSeekKeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPilot", "deepseek_api_key.txt");

    // ── 배포용 기본 DeepSeek API 키 ──
    // 소스에는 비워 두고, 릴리스 CI(.github/workflows/release.yml)가 빌드 시점에
    // GitHub Secret(DEEPSEEK_API_KEY) 값을 이 줄에 주입한다 → 키가 git 히스토리에 남지 않음.
    // 우선순위: 사용자가 저장한 AppData 파일 > 이 기본 키.
    // (사용자가 '삭제'하면 빈 파일이 기록되어 기본 키도 적용되지 않는다.)
    private const string DefaultDeepSeekKey = "";

    [ObservableProperty] private bool _isDarkTheme  = false;  // 기본: 라이트
    [ObservableProperty] private bool _isFontLarge  = false;  // 기본: 보통

    // 이미지 텍스트 추출 탭의 AI 교정에 사용
    [ObservableProperty] private string _deepSeekApiKey = "";
    [ObservableProperty] private string _deepSeekStatus = "";

    public bool IsDeepSeekKeySet => !string.IsNullOrWhiteSpace(DeepSeekApiKey);

    partial void OnDeepSeekApiKeyChanged(string value) => OnPropertyChanged(nameof(IsDeepSeekKeySet));

    // ── 미니 모드 전환 단축키 ──
    [ObservableProperty] private bool _isHotkeyEnabled = true;
    [ObservableProperty] private Key _hotkeyKey1 = Key.Space;
    [ObservableProperty] private Key _hotkeyKey2 = Key.Tab;
    [ObservableProperty] private bool _isCapturingHotkey;

    partial void OnIsHotkeyEnabledChanged(bool value)
    {
        PersistHotkey();
        HotkeyEnabledChanged?.Invoke(value);
    }

    public event Action<bool>? HotkeyEnabledChanged;

    public string HotkeyDisplayText => GetFriendlyKeyName(HotkeyKey1) + " + " + GetFriendlyKeyName(HotkeyKey2);

    partial void OnHotkeyKey1Changed(Key value) => OnPropertyChanged(nameof(HotkeyDisplayText));
    partial void OnHotkeyKey2Changed(Key value) => OnPropertyChanged(nameof(HotkeyDisplayText));

    private static string GetFriendlyKeyName(Key key)
    {
        return key switch
        {
            Key.Return   => "Enter",
            Key.Tab      => "Tab",
            Key.Space    => "Space",
            Key.Escape   => "Esc",
            Key.Back     => "Backspace",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LeftCtrl or Key.RightCtrl   => "Ctrl",
            Key.LeftAlt  or Key.RightAlt    => "Alt",
            Key.LWin     or Key.RWin        => "Win",
            Key.PageUp   => "PgUp",
            Key.PageDown => "PgDn",
            Key.Capital  => "CapsLock",
            Key.OemMinus => "-",
            Key.OemPlus  => "+",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.Divide   => "/",
            Key.Multiply => "*",
            Key.Subtract => "-",
            Key.Add      => "+",
            Key.Decimal  => ".",
            _ => key.ToString()
        };
    }

    /// <summary>Call from code-behind after capturing two keys via keyboard events.</summary>
    public void SetHotkey(Key key1, Key key2)
    {
        HotkeyKey1 = key1;
        HotkeyKey2 = key2;
        PersistHotkey();
        HotkeyChanged?.Invoke(HotkeyKey1, HotkeyKey2);
    }

    /// <summary>Fired when the user changes the hotkey. (Key1 vkCode, Key2 vkCode)</summary>
    public event Action<Key, Key>? HotkeyChanged;

    private void PersistHotkey()
    {
        var setting = new HotkeySetting
        {
            IsEnabled = IsHotkeyEnabled,
            Key1 = KeyInterop.VirtualKeyFromKey(HotkeyKey1),
            Key2 = KeyInterop.VirtualKeyFromKey(HotkeyKey2)
        };
        setting.Save();
    }

    public string CurrentVersionText => UpdateService.CurrentVersionText;

    private SettingsViewModel()
    {
        _deepSeekApiKey = LoadDeepSeekKey();
        LoadHotkeySetting();
        ApplyTheme(_isDarkTheme);
        ApplyFontSize(_isFontLarge);
    }

    private void LoadHotkeySetting()
    {
        var setting = HotkeySetting.Load();
        IsHotkeyEnabled = setting.IsEnabled;
        HotkeyKey1 = KeyInterop.KeyFromVirtualKey(setting.Key1);
        HotkeyKey2 = KeyInterop.KeyFromVirtualKey(setting.Key2);
    }

    partial void OnIsDarkThemeChanged(bool value)  => ApplyTheme(value);
    partial void OnIsFontLargeChanged(bool value)  => ApplyFontSize(value);

    // DeepSeek API 키는 관리자만 수정 가능 — 저장/삭제 전 관리자 비밀번호(0000) 확인
    [RelayCommand]
    private void SaveDeepSeekKey()
    {
        if (!WinPilot.Views.PasswordDialog.Verify())
        {
            DeepSeekStatus = "관리자 인증이 취소되어 저장하지 않았습니다.";
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_deepSeekKeyPath)!);
            var key = DeepSeekApiKey.Trim();
            // DPAPI(CurrentUser)로 암호화하여 저장 → 같은 Windows 계정에서만 복호화 가능
            File.WriteAllBytes(_deepSeekKeyPath, key.Length == 0 ? [] : Protect(key));
            DeepSeekStatus = "저장되었습니다. (암호화됨)";
        }
        catch (Exception ex) { DeepSeekStatus = $"저장 실패: {ex.Message}"; }
    }

    // DeepSeek API 키는 관리자만 수정 가능 — 저장/삭제 전 관리자 비밀번호(0000) 확인
    [RelayCommand]
    private void DeleteDeepSeekKey()
    {
        if (!WinPilot.Views.PasswordDialog.Verify())
        {
            DeepSeekStatus = "관리자 인증이 취소되어 삭제하지 않았습니다.";
            return;
        }
        try
        {
            // 빈 파일을 기록해 '삭제됨' 상태를 영구 저장 → 재시작/배포 기본 키보다 우선
            Directory.CreateDirectory(Path.GetDirectoryName(_deepSeekKeyPath)!);
            File.WriteAllBytes(_deepSeekKeyPath, []);
            DeepSeekApiKey = "";
            DeepSeekStatus = "키가 삭제되었습니다.";
        }
        catch (Exception ex) { DeepSeekStatus = $"삭제 실패: {ex.Message}"; }
    }

    // ── DPAPI 암호화/복호화 (CurrentUser 범위) ──
    private static byte[] Protect(string plain) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);

    private static string LoadDeepSeekKey()
    {
        try
        {
            // 저장 파일이 있으면(삭제로 비워진 경우 포함) 그 값이 우선 → 사용자가 같은 PC에서
            // 저장/삭제한 결과가 재시작 후에도 유지됨.
            if (File.Exists(_deepSeekKeyPath))
            {
                var bytes = File.ReadAllBytes(_deepSeekKeyPath);
                if (bytes.Length == 0) return "";   // 명시적 '삭제됨' 상태

                try
                {
                    var dec = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(dec).Trim();
                }
                catch
                {
                    // 구버전 평문 파일 → 평문으로 읽음 (다음 저장 시 자동으로 암호화됨)
                    return Encoding.UTF8.GetString(bytes).Trim();
                }
            }
        }
        catch { /* 무시하고 기본 키로 폴백 */ }

        // 저장 이력이 없는 신규 설치 → 배포용 기본 키 사용
        return DefaultDeepSeekKey.Trim();
    }

    public static void ApplyFontSize(bool isLarge)
    {
        var res = Application.Current.Resources;
        res["FontSm"] = isLarge ? 13.0 : 11.0;
        res["FontMd"] = isLarge ? 14.0 : 12.0;
        res["FontLg"] = isLarge ? 15.0 : 13.0;
        res["FontXl"] = isLarge ? 16.0 : 14.0;
    }

    public static void ApplyTheme(bool isDark)
    {
        var res = Application.Current.Resources;
        if (isDark)
        {
            res["BgBrush"]            = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            res["SurfaceBrush"]       = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3E));
            res["SidebarBrush"]       = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x2A));
            res["TextPrimaryBrush"]   = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            res["TextValueBrush"]     = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            res["ItemRowBrush"]       = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2A));
            res["AltRowBrush"]        = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x35));
            res["ControlBrush"]       = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            res["BorderBrush"]        = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            res["WarningBgBrush"]     = new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x10));
            res["ConsoleBgBrush"]     = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x1A));
            res["ConsoleTextBrush"]   = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }
        else
        {
            res["BgBrush"]            = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            res["SurfaceBrush"]       = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["SidebarBrush"]       = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextPrimaryBrush"]   = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            res["TextValueBrush"]     = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            res["ItemRowBrush"]       = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7));
            res["AltRowBrush"]        = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF5));
            res["ControlBrush"]       = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            res["BorderBrush"]        = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            res["WarningBgBrush"]     = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
            res["ConsoleBgBrush"]     = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            res["ConsoleTextBrush"]   = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }
    }
}
