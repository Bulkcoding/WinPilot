using System.Windows;

namespace WinPilot.Views;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    /// <summary>상태 문구 갱신 (선택적).</summary>
    public void SetStatus(string text) => StatusText.Text = text;
}
