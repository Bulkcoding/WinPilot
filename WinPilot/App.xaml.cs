using System.Text;
using System.Threading;
using System.Windows;

namespace WinPilot;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "WinPilot_SingleInstance", out bool isNew);

        if (!isNew)
        {
            // 이미 실행 중 → 기존 창 포커스 후 종료
            var existing = System.Diagnostics.Process.GetProcessesByName(
                System.Diagnostics.Process.GetCurrentProcess().ProcessName);
            foreach (var p in existing)
            {
                if (p.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                if (p.MainWindowHandle != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                break;
            }
            Shutdown();
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += (_, ex) =>
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinPilot_crash.log"),
                ex.Exception.ToString());
            MessageBox.Show(ex.Exception.ToString(), "WinPilot 오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
