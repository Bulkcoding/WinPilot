using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class RecoveryView : UserControl
{
    private ScrollViewer? _sv;
    // 사용자가 맨 아래에 있을 때만 자동 스크롤 (위로 올라가 있으면 보존)
    private bool _autoScroll = true;

    public RecoveryView() => InitializeComponent();

    // TextBox 안의 ScrollViewer를 비주얼 트리에서 탐색
    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(d, i));
            if (found != null) return found;
        }
        return null;
    }

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;

        // 첫 TextChanged 때 ScrollViewer 초기화 + ScrollChanged 구독
        if (_sv == null)
        {
            _sv = FindScrollViewer(tb);
            if (_sv != null)
                _sv.ScrollChanged += OnScrollChanged;
        }

        if (DataContext is RecoveryViewModel vm && vm.IsProgressUpdate)
        {
            // 진행률 줄 인플레이스 업데이트: 스크롤 위치 고정
            // Background 우선순위 = Render(7) 이후에 실행 → TextBox 자동스크롤을 덮어씀
            if (_sv is { } scrollViewer)
            {
                var offset = scrollViewer.VerticalOffset;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => scrollViewer.ScrollToVerticalOffset(offset)));
            }
            return;
        }

        // 새 줄 추가: 사용자가 맨 아래에 있을 때만 자동 스크롤
        if (_autoScroll)
            tb.ScrollToEnd();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // ExtentHeightChange == 0 → 콘텐츠 높이 변화 없음 = 사용자가 직접 스크롤
        // ExtentHeightChange != 0 → 텍스트 추가로 인한 높이 변화
        if (e.ExtentHeightChange == 0 && _sv != null)
            _autoScroll = _sv.VerticalOffset >= _sv.ScrollableHeight - 20;
    }
}
