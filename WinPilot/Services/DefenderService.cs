using System.Diagnostics;
using Microsoft.Win32;

namespace WinPilot.Services;

public enum ExclusionType { Path, Process, Extension }

public record DefenderExclusion(string Value, ExclusionType Type);

public static class DefenderService
{
    private const string PathKey      = @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths";
    private const string ProcessKey   = @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Processes";
    private const string ExtensionKey = @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Extensions";
    private const string TamperKey    = @"SOFTWARE\Microsoft\Windows Defender\Features";

    /// <summary>
    /// Tamper Protection(변조 방지)이 켜져 있으면 true.
    /// 값이 5이면 ON — 이 경우 외부 프로세스에서 제외 목록 수정 불가.
    /// </summary>
    public static bool IsTamperProtectionEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TamperKey);
            return key?.GetValue("TamperProtection") is int v && v == 5;
        }
        catch { return false; }
    }

    /// <summary>
    /// 현재 Defender 제외 목록을 레지스트리에서 읽어 반환합니다.
    /// 레지스트리의 값 이름(name) 자체가 제외 경로입니다.
    /// </summary>
    public static List<DefenderExclusion> GetExclusions()
    {
        var result = new List<DefenderExclusion>();
        ReadFromKey(PathKey,      ExclusionType.Path,      result);
        ReadFromKey(ProcessKey,   ExclusionType.Process,   result);
        ReadFromKey(ExtensionKey, ExclusionType.Extension, result);
        return result;
    }

    private static void ReadFromKey(string keyPath, ExclusionType type, List<DefenderExclusion> list)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(new DefenderExclusion(name, type));
        }
        catch { /* 읽기 권한 없으면 무시 */ }
    }

    /// <summary>
    /// Add-MpPreference cmdlet으로 제외 항목을 추가합니다.
    /// Tamper Protection이 OFF인 경우에만 성공합니다.
    /// </summary>
    public static async Task<bool> AddExclusionAsync(string value, ExclusionType type)
    {
        string param = type switch
        {
            ExclusionType.Path      => "ExclusionPath",
            ExclusionType.Process   => "ExclusionProcess",
            ExclusionType.Extension => "ExclusionExtension",
            _                       => "ExclusionPath"
        };
        return await RunPsAsync($"Add-MpPreference -{param} '{EscapePs(value)}'");
    }

    /// <summary>
    /// Remove-MpPreference cmdlet으로 제외 항목을 삭제합니다.
    /// </summary>
    public static async Task<bool> RemoveExclusionAsync(string value, ExclusionType type)
    {
        string param = type switch
        {
            ExclusionType.Path      => "ExclusionPath",
            ExclusionType.Process   => "ExclusionProcess",
            ExclusionType.Extension => "ExclusionExtension",
            _                       => "ExclusionPath"
        };
        return await RunPsAsync($"Remove-MpPreference -{param} '{EscapePs(value)}'");
    }

    private static Task<bool> RunPsAsync(string command) => Task.Run(() =>
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command}\"")
            {
                UseShellExecute  = false,
                CreateNoWindow   = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });

    // PowerShell 문자열 내 작은따옴표 이스케이프
    private static string EscapePs(string s) => s.Replace("'", "''");
}
