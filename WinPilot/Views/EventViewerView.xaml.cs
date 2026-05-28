using System.Windows.Controls;
using System.Windows.Input;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class EventViewerView : UserControl
{
    public EventViewerView() => InitializeComponent();

    private void EventGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EventViewerViewModel vm && vm.SelectedEntry != null)
            vm.IsDetailVisible = true;
    }

    // 설명 TextBox 마우스 휠이 외부 ScrollViewer로 버블링되지 않도록 차단하고 직접 스크롤
    private void DescriptionTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var tb = (TextBox)sender;
        var sv = FindScrollViewer(tb);
        sv?.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
    }

    private static ScrollViewer? FindScrollViewer(System.Windows.DependencyObject d)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
