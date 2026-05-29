using CommunityToolkit.Mvvm.ComponentModel;

namespace WinPilot.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName    => "WinPilot";
    public string Version    => "v1.0.0";
    public string Description => "원격 PC 상태 확인을 위한 WPF 기반 모니터링 도구";
    public string Author     => "Bulkcoding";
    public string Runtime    => $".NET {System.Environment.Version}";
    public string BuildDate  => "2025";
}
