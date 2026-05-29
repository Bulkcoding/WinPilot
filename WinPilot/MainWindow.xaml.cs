using System.ComponentModel;
using System.Windows;
using WinPilot.ViewModels;

namespace WinPilot;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMiniMode)) return;
        var isMini = ((MainViewModel)sender!).IsMiniMode;
        if (isMini)
        {
            SizeToContent = SizeToContent.Height;
            Width = 340;
            ResizeMode = ResizeMode.CanMinimize;
            Topmost = true;
        }
        else
        {
            Topmost = false;
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = 1100;
            Height = 720;
        }
    }
}