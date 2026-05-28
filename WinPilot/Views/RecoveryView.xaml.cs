using System.Windows.Controls;

namespace WinPilot.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView() => InitializeComponent();

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => ((TextBox)sender).ScrollToEnd();
}
