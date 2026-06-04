using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;

namespace WinPilot.Services;

public class RecoveryService
{
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RestartAsAdmin()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true });
        }
        catch { return; }
        System.Windows.Application.Current.Shutdown();
    }

    public Process? StartCommand(string fileName, string arguments,
        DataReceivedEventHandler? outputHandler, EventHandler? exitedHandler)
    {
        // sfc.exe 는 stdout 리다이렉트 시 UTF-16 LE 로 출력함 (한글 Windows 포함)
        // dism.exe 는 OEM 코드 페이지(CP949 등)로 출력하므로 chcp 65001 + UTF-8 로 처리
        bool isSfc = fileName.Equals("sfc", StringComparison.OrdinalIgnoreCase);

        ProcessStartInfo info;
        if (isSfc)
        {
            info = new ProcessStartInfo
            {
                FileName               = fileName,
                Arguments              = arguments,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.Unicode,   // UTF-16 LE
                StandardErrorEncoding  = Encoding.Unicode
            };
        }
        else
        {
            // DISM: cmd + chcp 65001 → UTF-8 강제
            info = new ProcessStartInfo
            {
                FileName               = "cmd.exe",
                Arguments              = $"/c \"chcp 65001 >nul 2>&1 & {fileName} {arguments}\"",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (outputHandler != null)
        {
            process.OutputDataReceived += outputHandler;
            process.ErrorDataReceived += outputHandler;
        }
        if (exitedHandler != null)
            process.Exited += exitedHandler;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch
        {
            process.Dispose();
            return null;
        }
    }
}
