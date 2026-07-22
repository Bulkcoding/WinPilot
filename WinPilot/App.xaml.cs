using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinPilot.Services;
using WinPilot.Views;

namespace WinPilot;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TrayService? _tray;

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

        // 시작 스플래시(로딩 진행 표시) — 먼저 띄우고 즉시 렌더해서 로딩 중임을 보여줌
        var splash = new SplashWindow();
        splash.Show();
        splash.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        // 메인 윈도우 생성 (StartupUri 제거 → 스플래시가 MainWindow로 잡히지 않도록 명시적으로 지정)
        var main = new MainWindow();
        MainWindow = main;

        // 스플래시가 뜬 모니터의 중앙에 메인 창을 배치.
        // 스플래시는 CenterScreen이라 그 중심 = 해당 모니터 중심 → 메인을 같은 중심점에 맞추면
        // 동일 모니터 중앙에 뜬다. (같은 WPF 좌표계라 DPI/멀티모니터에도 일관)
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        if (!double.IsNaN(splash.Left) && !double.IsNaN(splash.Top))
        {
            double centerX = splash.Left + splash.Width / 2;
            double centerY = splash.Top  + splash.Height / 2;
            main.Left = centerX - main.Width / 2;
            main.Top  = centerY - main.Height / 2;
        }

        main.ContentRendered += (_, _) =>
        {
            splash.Close();
            main.Activate();
        };
        main.Show();

        // MainWindow가 생성된 후 트레이 아이콘 연결
        _tray = new TrayService();
        _tray.Attach(main);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
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
