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
            // DISM은 시스템 OEM 코드페이지(한국어: 949)로 출력함
            // chcp 65001 + UTF-8 은 실제로 바이트 자체를 바꾸지 않으므로 OEM 인코딩으로 직접 디코딩
            var oemEncoding = Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            info = new ProcessStartInfo
            {
                FileName               = "dism.exe",
                Arguments              = arguments,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = oemEncoding,
                StandardErrorEncoding  = oemEncoding
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

    /// <summary>
    /// SFC처럼 stdout을 버퍼링하는 프로세스용: 종료 후 전체 출력을 행 단위로 반환.
    /// stdoutOnly=true 시 stderr를 읽되 버리므로 SFC처럼 두 스트림에 중복 출력하는 프로세스에 사용.
    /// </summary>
    public async Task<(int ExitCode, List<string> Lines)> RunAndCaptureAsync(
        string fileName, string arguments, bool stdoutOnly = false)
    {
        var info = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding  = Encoding.Unicode
        };

        using var p = new Process { StartInfo = info };
        p.Start();

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync(); // 버퍼 블로킹 방지용 항상 읽음
        await p.WaitForExitAsync();

        var stdout = await outTask;
        var stderr = await errTask;
        var text = stdoutOnly ? stdout : stdout + (stderr.Length > 0 ? "\n" + stderr : "");

        var lines = text
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        return (p.ExitCode, lines);
    }
}
