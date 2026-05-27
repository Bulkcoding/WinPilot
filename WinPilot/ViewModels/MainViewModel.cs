using CommunityToolkit.Mvvm.ComponentModel;

namespace WinPilot.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "WinPilot";
}
