using System.Windows;
using System.Windows.Input;

namespace WinPilot.Views;

public partial class PasswordDialog : Window
{
    // 관리자 비밀번호 (배포 시 관리자만 DeepSeek API 키를 수정 가능)
    private const string AdminPassword = "0000";

    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PwdBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => TryConfirm();

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryConfirm();
    }

    private void TryConfirm()
    {
        if (PwdBox.Password == AdminPassword)
        {
            DialogResult = true;
        }
        else
        {
            ErrorText.Visibility = Visibility.Visible;
            PwdBox.Clear();
            PwdBox.Focus();
        }
    }

    /// <summary>관리자 비밀번호 확인 팝업을 띄우고, 인증 성공 시 true를 반환한다.</summary>
    public static bool Verify()
    {
        var dlg = new PasswordDialog { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true;
    }
}
