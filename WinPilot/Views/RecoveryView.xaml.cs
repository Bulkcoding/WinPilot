using System;
using System.Windows.Controls;
using System.Windows.Threading;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView() => InitializeComponent();

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        if (DataContext is RecoveryViewModel vm && vm.IsProgressUpdate)
        {
            // 진행률 업데이트: 텍스트 변경 후 WPF가 자동 스크롤하는 것을 막기 위해
            // 현재 offset을 저장했다가 레이아웃 업데이트 후 복원
            var offset = tb.VerticalOffset;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => tb.ScrollToVerticalOffset(offset)));
            return;
        }
        tb.ScrollToEnd();
    }
}
