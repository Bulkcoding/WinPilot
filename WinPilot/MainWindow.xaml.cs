using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WinPilot.Models;
using WinPilot.Services;
using WinPilot.ViewModels;

namespace WinPilot;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const uint SWP_NOZORDER    = 0x0004;
    private const uint SWP_NOSIZE      = 0x0001;
    private const uint SWP_NOACTIVATE  = 0x0010;
    private const uint SWP_NOCOPYBITS  = 0x0100;   // 리사이즈 시 기존 비트맵 복사 안 함 → 잔상 제거
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private readonly GlobalHotkeyService _hotkeyService = new();

    public MainWindow()
    {
        InitializeComponent();
        Icon = CreateWIcon();

        Loaded += (_, _) =>
        {
            // Windows 11: 둥근 모서리
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;

                // 글로벌 단축키 초기화
                var settings = vm.Settings;
                var setting = HotkeySetting.Load();
                _hotkeyService.SetFromSetting(setting);
                _hotkeyService.HotkeyTriggered += () =>
                {
                    Dispatcher.Invoke(() => vm.ToggleMiniModeCommand.Execute(null));
                };

                if (settings.IsHotkeyEnabled)
                    _hotkeyService.Start();

                settings.HotkeyChanged += (key1, key2) =>
                {
                    _hotkeyService.SetFromSetting(new HotkeySetting
                    {
                        IsEnabled = settings.IsHotkeyEnabled,
                        Key1 = KeyInterop.VirtualKeyFromKey(key1),
                        Key2 = KeyInterop.VirtualKeyFromKey(key2)
                    });
                };

                settings.HotkeyEnabledChanged += enabled =>
                {
                    if (enabled) _hotkeyService.Start();
                    else _hotkeyService.Stop();
                };
            }
        };

        Closed += (_, _) => _hotkeyService.Dispose();
    }

    // 창 컨트롤 버튼
    private void MinimizeClick(object sender, System.Windows.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestoreClick(object sender, System.Windows.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, System.Windows.RoutedEventArgs e)
        => Close();

    // W 타일 아이콘 생성 (코드로 직접)
    private static BitmapSource CreateWIcon(int size = 64)
    {
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(
                Color.FromRgb(0x3B, 0x82, 0xF6),
                Color.FromRgb(0x1D, 0x4E, 0xD8),
                new Point(0, 0), new Point(1, 1));
            var r = size * 0.18;
            ctx.DrawRoundedRectangle(brush, null, new Rect(0, 0, size, size), r, r);
            var text = new FormattedText("W",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                size * 0.54, Brushes.White, 96);
            ctx.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        target.Render(visual);
        return target;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMiniMode)) return;
        if (((MainViewModel)sender!).IsMiniMode)
            AnimateToMini();
        else
            AnimateToNormal();
    }

    private const double MiniWidth   = 320;
    private const double NormalWidth = 1100;
    private const double NormalHeight = 720;
    private const double TitleBarHeight = 34;   // MainWindow.xaml RowDefinitions[0]

    private void AnimateToMini()
    {
        // 애니메이션 중 320 너비가 막히지 않도록 최소 크기·모드 먼저 해제
        MinWidth = 0; MinHeight = 0;
        ResizeMode = ResizeMode.CanMinimize;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;   // 높이를 직접 애니메이션할 수 있도록

        // 미니 콘텐츠 자연 높이 측정: 타이틀바(34px) + 미니 패널 콘텐츠
        MiniPanel.Measure(new Size(MiniWidth, double.PositiveInfinity));
        double targetHeight = TitleBarHeight + MiniPanel.DesiredSize.Height;

        AnimateWindow(MiniWidth, targetHeight);
    }

    private void AnimateToNormal()
    {
        Topmost = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        SizeToContent = SizeToContent.Manual;
        Height = ActualHeight;   // 현재 높이를 명시적으로 고정(이전 SizeToContent 흔적 제거)
        // MinWidth/MinHeight는 애니메이션이 끝난 뒤 적용 (지금 적용하면 즉시 스냅됨)

        AnimateWindow(NormalWidth, NormalHeight, onDone: () =>
        {
            MinWidth = 900; MinHeight = 600;
            ClampToCurrentMonitor();
        });
    }

    /// <summary>
    /// 창이 걸쳐 있는 "현재 모니터"의 작업 영역 안으로만 위치를 보정한다.
    /// 계산을 전부 Win32 물리 픽셀 좌표로 통일 — 애니메이션도 SetWindowPos(물리)로 하므로
    /// 좌표계가 일치한다. (기존엔 WPF DIP ÷ DPI 변환을 섞어 써서 다른 DPI의 보조 모니터에서
    /// 좌표가 어긋나 주모니터로 튕기던 문제가 있었음.)
    /// </summary>
    private void ClampToCurrentMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var wr)) return;

        var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return;
        var work = mi.rcWork;

        int w = wr.Right - wr.Left;
        int h = wr.Bottom - wr.Top;
        int left = wr.Left, top = wr.Top;

        if (left + w > work.Right)  left = Math.Max(work.Left, work.Right  - w);
        if (top  + h > work.Bottom) top  = Math.Max(work.Top,  work.Bottom - h);
        if (left < work.Left) left = work.Left;
        if (top  < work.Top)  top  = work.Top;

        if (left != wr.Left || top != wr.Top)
            SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);

        // WPF Left/Top을 실제 물리 위치와 동기화 (다음 미니 전환이 stale 좌표를 쓰지 않도록).
        // 애니메이션이 Left*DpiScale로 물리 좌표를 만들므로, 역변환도 동일 배율로 나눠 일관성 유지.
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = left / dpi.DpiScaleX;
        Top  = top  / dpi.DpiScaleY;
    }

    private const double ResizeDurationMs = 360;
    private EventHandler? _resizeTick;

    /// <summary>
    /// Win32 SetWindowPos로 매 프레임 위치+크기를 한 번에 변경한다.
    /// WPF Width/Height 속성은 각각 별도 리사이즈로 처리되어 가로/세로가 계단식으로 보이는 반면,
    /// SetWindowPos는 cx·cy를 atomic하게 적용 → 우하단 모서리가 직선(대각선) 경로로 이동.
    /// </summary>
    private void AnimateWindow(double toWidth, double toHeight, Action? onDone = null)
    {
        if (_resizeTick != null)   // 진행 중 애니메이션 정리
        {
            CompositionTarget.Rendering -= _resizeTick;
            _resizeTick = null;
        }

        // 리사이즈 중에는 콘텐츠를 숨긴다 → 창 단색 배경(BgBrush)만 대각선으로 변함.
        // WPF 렌더가 빠른 SetWindowPos 리사이즈를 못 따라가 생기는 잔상을 원천 차단.
        HideContent();

        // 바뀐 콘텐츠(Visibility)의 레이아웃을 애니메이션 시작 전에 미리 완료(숨긴 채로)
        UpdateLayout();

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var dpi  = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

        double fromW = ActualWidth, fromH = ActualHeight;
        double left  = Left,        top   = Top;   // 좌상단 고정 → 우하단 모서리만 대각선 이동
        int px = (int)Math.Round(left * sx);
        int py = (int)Math.Round(top  * sy);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _resizeTick = (_, _) =>
        {
            double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / ResizeDurationMs);
            double e = EaseInOut(t);
            double w = fromW + (toWidth  - fromW) * e;
            double h = fromH + (toHeight - fromH) * e;

            // 위치+크기를 단일 호출로 동시 적용 (NOCOPYBITS로 낡은 비트맵 잔상 방지)
            SetWindowPos(hwnd, IntPtr.Zero, px, py,
                (int)Math.Round(w * sx), (int)Math.Round(h * sy),
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS);

            if (t >= 1.0)
            {
                CompositionTarget.Rendering -= _resizeTick;
                _resizeTick = null;
                // WPF 속성을 실제 크기와 동기화 → 이후 레이아웃·수동 리사이즈 정상
                Width  = toWidth;
                Height = toHeight;
                onDone?.Invoke();
                FadeInContent();   // 크기 확정 후 콘텐츠를 부드럽게 표시
            }
        };
        CompositionTarget.Rendering += _resizeTick;
    }

    // 리사이즈 중 콘텐츠 숨김.
    // Visibility=Hidden은 요소를 안 그리므로 그 위 Adorner(점선/FocusVisual)까지 사라진다.
    // (Opacity=0은 Adorner를 못 숨겨 빈 화면 위에 점선이 떠 보였음)
    private void HideContent()
    {
        Keyboard.ClearFocus();
        NormalHost.BeginAnimation(OpacityProperty, null);
        MiniHost.BeginAnimation(OpacityProperty, null);
        NormalHost.Opacity = 0;
        MiniHost.Opacity   = 0;
        NormalHost.Visibility = Visibility.Hidden;
        MiniHost.Visibility   = Visibility.Hidden;
    }

    // 크기 조정 완료 후 콘텐츠 페이드 인 (안쪽 패널 중 현재 모드 것만 실제로 보임)
    private void FadeInContent()
    {
        NormalHost.Visibility = Visibility.Visible;
        MiniHost.Visibility   = Visibility.Visible;

        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)));
        NormalHost.BeginAnimation(OpacityProperty, anim);
        MiniHost.BeginAnimation(OpacityProperty, anim);
    }

    // 퀸틱 ease-in-out (0~1) — 양 끝이 더 완만해 부드럽게 가속/정착
    private static double EaseInOut(double t) =>
        t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
}
