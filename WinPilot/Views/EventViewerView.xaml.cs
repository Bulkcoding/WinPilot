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
}
