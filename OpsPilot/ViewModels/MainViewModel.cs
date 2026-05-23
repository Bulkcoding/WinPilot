using CommunityToolkit.Mvvm.ComponentModel;

namespace OpsPilot.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "OpsPilot";
}
