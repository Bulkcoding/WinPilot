using System.Diagnostics;

namespace WinPilot.Services;

public static class RegistryService
{
    public static List<(string Name, string Path, string Description)> GetDefaultShortcuts() =>
    [
        ("자동 시작 프로그램 (시스템)",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "시스템 전체 자동 시작 프로그램 목록"),
        ("자동 시작 프로그램 (사용자)",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            "현재 사용자 자동 시작 프로그램"),
        ("서비스 목록",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services",
            "Windows 서비스 레지스트리 키"),
        ("OS 버전 정보",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "운영체제 버전 및 빌드 정보"),
        ("설치된 프로그램",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            "설치된 소프트웨어 목록 (64비트)"),
        ("설치된 프로그램 (32비트)",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            "설치된 소프트웨어 목록 (32비트)"),
        ("Shell 폴더",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
            "사용자 폴더 경로 설정"),
        ("시스템 환경 변수",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
            "시스템 환경 변수"),
        ("사용자 환경 변수",
            @"HKEY_CURRENT_USER\Environment",
            "현재 사용자 환경 변수"),
        ("네트워크 어댑터",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
            "네트워크 어댑터 드라이버 설정"),
        ("Windows 방화벽",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy",
            "Windows 방화벽 프로파일 설정"),
        ("그룹 정책 (컴퓨터)",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft",
            "컴퓨터 그룹 정책"),
    ];

    public static void OpenInRegedit(string registryPath)
    {
        try
        {
            var setKey = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"add \"HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Applets\\Regedit\" /v LastKey /d \"{registryPath}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            setKey.Start();
            setKey.WaitForExit();
        }
        catch { }

        try
        {
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch { }
    }
}
