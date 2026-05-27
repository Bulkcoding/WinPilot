using System.Diagnostics;
using System.Security.Principal;

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
        var oemEncoding = System.Text.Encoding.GetEncoding(
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = oemEncoding,
            StandardErrorEncoding  = oemEncoding
        };

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
