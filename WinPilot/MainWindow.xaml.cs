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
    // DWM API — 타이틀바를 다크 모드로 전환
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();

        // 프로그래밍으로 W 타일 아이콘 생성 (이미지 파일 불필요)
        Icon = CreateWIcon();

        Loaded += (_, _) =>
        {
            ApplyDarkTitleBar();

            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    /// <summary>
    /// DWM API로 타이틀바 색상을 앱 테마(다크)에 맞춥니다.
    /// Windows 10 20H1 이상에서 동작합니다.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int val = 1;
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win10 20H1+)
        // 구버전 폴백 = 19
        if (DwmSetWindowAttribute(hwnd, 20, ref val, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref val, sizeof(int));
    }

    /// <summary>
    /// WPF DrawingVisual로 파란 그라디언트 W 타일 아이콘을 생성합니다.
    /// icon.png / icon_logo.png 없이 코드만으로 동작합니다.
    /// </summary>
    private static BitmapSource CreateWIcon(int size = 64)
    {
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var ctx = visual.RenderOpen())
        {
            // 파란 그라디언트 둥근 타일
            var brush = new LinearGradientBrush(
                Color.FromRgb(0x3B, 0x82, 0xF6),
                Color.FromRgb(0x1D, 0x4E, 0xD8),
                new Point(0, 0), new Point(1, 1));

            var radius = size * 0.18;
            ctx.DrawRoundedRectangle(brush, null,
                new Rect(0, 0, size, size), radius, radius);

            // 흰색 W 텍스트 (중앙 정렬)
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal, FontWeights.Black, FontStretches.Normal);

            var text = new FormattedText("W",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size * 0.54,
                Brushes.White,
                96);

            ctx.DrawText(text, new Point(
                (size - text.Width)  / 2,
                (size - text.Height) / 2));
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
