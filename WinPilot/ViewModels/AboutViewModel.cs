using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinPilot.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName => "WinPilot";
    public string Version => "v1.0.0";
    public string Description => "원격 PC 상태 확인 도구";
    public string GitHubUrl => "https://github.com/Bulkcoding/WinPilot";
    public string Author => "Bulkcoding";

    [RelayCommand]
    private void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
    }
}
