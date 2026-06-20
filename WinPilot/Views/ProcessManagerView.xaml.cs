using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class ProcessManagerView : UserControl
{
    public ProcessManagerView() => InitializeComponent();

    // DataGrid 기본 정렬 차단 → ViewModel의 그룹 단위 정렬로 위임
    private void ProcessDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;  // 기본 flat 정렬 취소

        var column = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(column)) return;

        if (DataContext is ProcessManagerViewModel vm)
            vm.SortByCommand.Execute(column);
    }
}
