using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class RegexView : UserControl
{
    private bool _updatingHighlights;
    private static readonly SolidColorBrush HighlightBrush =
        new(Color.FromArgb(100, 59, 130, 246)); // blue, works on both light/dark themes

    public RegexView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RegexViewModel vm) return;
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;

        // View가 재생성될 때 VM 상태 복원
        SyncRtbFromVm(vm.TestInput);
        ApplyHighlights(vm);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RegexViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegexViewModel.MatchRanges) && DataContext is RegexViewModel vm)
            ApplyHighlights(vm);
    }

    // 사용자가 RTB에 입력하면 VM으로 반영
    private void OnTestRtbTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingHighlights) return;
        if (DataContext is not RegexViewModel vm) return;

        var raw = new TextRange(TestRtb.Document.ContentStart, TestRtb.Document.ContentEnd).Text;
        // FlowDocument: 단락 구분 = \r\n, LineBreak = \n → 정규화
        vm.TestInput = raw.Replace("\r\n", "\n").TrimEnd('\n');
        // MatchRanges 업데이트는 Evaluate() → PropertyChanged → ApplyHighlights 경로로 처리
    }

    // ── 하이라이트 적용 ──────────────────────────────────────

    private void ApplyHighlights(RegexViewModel vm)
    {
        if (_updatingHighlights) return;
        _updatingHighlights = true;
        try
        {
            // 기존 배경색 초기화
            new TextRange(TestRtb.Document.ContentStart, TestRtb.Document.ContentEnd)
                .ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);

            // 매치 구간에 파란 배경 적용
            foreach (var (index, length) in vm.MatchRanges)
            {
                if (length == 0) continue;
                var startPtr = GetPointerAtCharIndex(index);
                var endPtr   = GetPointerAtCharIndex(index + length);
                if (startPtr == null || endPtr == null) continue;
                new TextRange(startPtr, endPtr)
                    .ApplyPropertyValue(TextElement.BackgroundProperty, HighlightBrush);
            }
        }
        finally { _updatingHighlights = false; }
    }

    // ── RTB ↔ VM 텍스트 동기화 ──────────────────────────────

    private void SyncRtbFromVm(string text)
    {
        _updatingHighlights = true;
        try
        {
            var doc = TestRtb.Document;
            doc.Blocks.Clear();

            var lines = text.Length == 0 ? [""] : text.Split('\n');
            foreach (var line in lines)
                doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0) });
        }
        finally { _updatingHighlights = false; }
    }

    // ── TextPointer 위치 계산 ────────────────────────────────

    // 정규화된 문자 인덱스(charIndex) → FlowDocument TextPointer
    // 단락 구분은 \n 1글자로 계산
    private TextPointer? GetPointerAtCharIndex(int charIndex)
    {
        foreach (Block block in TestRtb.Document.Blocks)
        {
            if (block is not Paragraph para) continue;

            int paraLen = CountTextChars(para);
            if (charIndex <= paraLen)
                return GetPointerInParagraphAt(para, charIndex);

            charIndex -= paraLen + 1; // +1 = 단락 간 \n
        }
        return TestRtb.Document.ContentEnd;
    }

    private static int CountTextChars(Paragraph para)
    {
        int count = 0;
        var pos = para.ContentStart;
        while (pos != null && pos.CompareTo(para.ContentEnd) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                count += pos.GetTextRunLength(LogicalDirection.Forward);
            pos = pos.GetNextContextPosition(LogicalDirection.Forward);
        }
        return count;
    }

    private static TextPointer? GetPointerInParagraphAt(Paragraph para, int offset)
    {
        var pos = para.ContentStart;
        while (pos != null && pos.CompareTo(para.ContentEnd) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = pos.GetTextRunLength(LogicalDirection.Forward);
                if (len >= offset)
                    return pos.GetPositionAtOffset(offset);
                offset -= len;
            }
            pos = pos.GetNextContextPosition(LogicalDirection.Forward);
        }
        return para.ContentEnd;
    }
}
