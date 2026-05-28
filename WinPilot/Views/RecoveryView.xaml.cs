using System.Windows.Controls;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView() => InitializeComponent();

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        if (DataContext is RecoveryViewModel vm && vm.IsProgressUpdate) return;
        tb.ScrollToEnd();
    }
}
