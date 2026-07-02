using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPilot.ViewModels;

namespace WinPilot.Views;

file record ImageHistoryItem(BitmapSource Image, string Label);

public partial class OcrView : UserControl
{
    private OcrViewModel? Vm => DataContext as OcrViewModel;

    // 최신순, 최대 6개 (현재 1 + 이전 5)
    private readonly List<BitmapSource> _imageHistory = [];

    private DispatcherTimer? _timerExtracted;
    private DispatcherTimer? _timerTranslated;

    public OcrView() => InitializeComponent();

    // ─── 키보드 ──────────────────────────────────────────
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ZoomOverlay.Visibility           == Visibility.Visible) { ZoomOverlay.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (ImagePickerOverlay.Visibility    == Visibility.Visible) { ImagePickerOverlay.Visibility = Visibility.Collapsed; e.Handled = true; return; }
        }

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && Keyboard.FocusedElement is not TextBox
            && Clipboard.ContainsImage())
        {
            var bmp = Clipboard.GetImage();
            if (bmp != null) RecordAndOcr(bmp);
            e.Handled = true;
        }
    }

    // ─── 이미지 영역 클릭 → 클립보드 이미지 바로 붙여넣기 ─
    private void OnImageAreaClick(object sender, MouseButtonEventArgs e)
    {
        if (!Clipboard.ContainsImage()) return;
        var bmp = Clipboard.GetImage();
        if (bmp != null) RecordAndOcr(bmp);
    }

    private void RecordAndOcr(BitmapSource bitmap)
    {
        // 같은 참조 중복 제거 후 최신순 삽입, 최대 6개
        _imageHistory.RemoveAll(b => ReferenceEquals(b, bitmap));
        _imageHistory.Insert(0, bitmap);
        if (_imageHistory.Count > 6) _imageHistory.RemoveAt(6);

        PreviewImage.Source     = bitmap;
        PreviewImage.Visibility = Visibility.Visible;
        PasteHint.Visibility    = Visibility.Collapsed;
        ZoomBtn.Visibility      = Visibility.Visible;

        // 이전 이미지가 1개 이상 있어야 "이전" 버튼 표시
        PrevImagesBtn.Visibility = _imageHistory.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

        _ = Vm?.ExtractTextAsync(bitmap);
    }

    // ─── 이전 이미지 팝업 ────────────────────────────────
    private void OnPreviousImagesClick(object sender, RoutedEventArgs e)
    {
        // 현재(index 0) 제외, 최대 5개
        var prev = _imageHistory.Skip(1).Take(5)
            .Select((img, i) => new ImageHistoryItem(img, $"{i + 1}번째 이전"))
            .ToList();

        if (prev.Count == 0) return;

        ClipboardImageList.ItemsSource = prev;
        PickerSubtitle.Text = $"{prev.Count}개의 이전 이미지  ·  마우스를 올리면 크게 볼 수 있습니다";
        ImagePickerOverlay.Visibility = Visibility.Visible;
    }

    private void OnImagePickerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClipboardImageList.SelectedItem is not ImageHistoryItem item) return;
        ImagePickerOverlay.Visibility  = Visibility.Collapsed;
        ClipboardImageList.SelectedItem = null;
        RecordAndOcr(item.Image);
    }

    private void OnCloseImagePicker(object sender, RoutedEventArgs e)
        => ImagePickerOverlay.Visibility = Visibility.Collapsed;

    private void OnPickerOverlayClick(object sender, MouseButtonEventArgs e)
        => ImagePickerOverlay.Visibility = Visibility.Collapsed;

    private void OnPickerPanelClick(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    // ─── 이미지 확대 ─────────────────────────────────────
    private void OnZoomOpen(object sender, RoutedEventArgs e)
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;
        ZoomedImage.Source        = bmp;
        ZoomOverlay.Visibility    = Visibility.Visible;
    }

    private void OnZoomClose(object sender, RoutedEventArgs e)
        => ZoomOverlay.Visibility = Visibility.Collapsed;

    // MouseLeftButtonDown 오버로드 (오버레이 배경 클릭)
    private void OnZoomClose(object sender, MouseButtonEventArgs e)
        => ZoomOverlay.Visibility = Visibility.Collapsed;

    // ─── 복사 피드백 ─────────────────────────────────────
    private void OnCopyExtracted(object sender, RoutedEventArgs e)
    {
        if (!TryCopy(Vm?.ExtractedText)) return;
        FlashAndShowFeedback(CopyBtnExtracted, CopyFeedbackExtracted, ref _timerExtracted);
    }

    private void OnCopyTranslated(object sender, RoutedEventArgs e)
    {
        if (!TryCopy(Vm?.TranslatedText)) return;
        FlashAndShowFeedback(CopyBtnTranslated, CopyFeedbackTranslated, ref _timerTranslated);
    }

    private static void FlashAndShowFeedback(Button button, TextBlock feedback, ref DispatcherTimer? existing)
    {
        existing?.Stop();

        button.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromMilliseconds(300))));

        feedback.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) => { timer.Stop(); feedback.Visibility = Visibility.Collapsed; };
        timer.Start();
        existing = timer;
    }

    private static bool TryCopy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try { Clipboard.SetText(text); return true; }
        catch { return false; }
    }
}
