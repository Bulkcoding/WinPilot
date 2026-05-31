using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPilot.ViewModels;

namespace WinPilot;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
        Icon = CreateWIcon();

        Loaded += (_, _) =>
        {
            // Windows 11: 둥근 모서리 (DWM)
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    // ─── 사이드바 헤더 드래그 (= 타이틀바 역할) ──────────────────────

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private void SidebarHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // 버튼 클릭이면 드래그 안 함 (toggle 버튼 등)
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null)
        {
            if (dep is System.Windows.Controls.Primitives.ButtonBase) return;
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }

        if (e.ClickCount == 2)
        {
            MaximizeRestore();
            return;
        }

        // OS 레벨 드래그 (DragMove보다 안정적)
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        var hwnd = new WindowInteropHelper(this).Handle;
        ReleaseCapture();
        SendMessage(hwnd, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    // ─── 창 컨트롤 버튼 ─────────────────────────────────────────────

    private void MinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestoreClick(object sender, RoutedEventArgs e)
        => MaximizeRestore();

    private void CloseClick(object sender, RoutedEventArgs e)
        => Close();

    private void MaximizeRestore()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // ─── W 타일 아이콘 (코드로 직접 생성) ───────────────────────────

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
                new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                size * 0.54, Brushes.White, 96);

            ctx.DrawText(text, new Point(
                (size - text.Width)  / 2,
                (size - text.Height) / 2));
        }

        target.Render(visual);
        return target;
    }

    // ─── 미니 모드 전환 ──────────────────────────────────────────────

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMiniMode)) return;
        var isMini = ((MainViewModel)sender!).IsMiniMode;
        if (isMini)
        {
            MinWidth = 0; MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            Width = 320;
            ResizeMode = ResizeMode.CanMinimize;
            Topmost = true;
        }
        else
        {
            Topmost = false;
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 900; MinHeight = 600;
            Width = 1100; Height = 720;
        }
    }
}
