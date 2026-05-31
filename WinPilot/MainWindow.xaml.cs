using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
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
            // Windows 11: 둥근 모서리
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
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
