using CommunityToolkit.Mvvm.ComponentModel;

namespace WinPilot.ViewModels;

/// <summary>
/// 유틸리티 모음 탭. 서브탭으로 개별 도구(자식 VM)를 보유한다.
/// 새 유틸 추가 = 여기에 자식 VM 속성 + UtilesView TabItem + App.xaml DataTemplate.
/// </summary>
public partial class UtilesViewModel : ObservableObject
{
    public RegexViewModel Regex { get; } = new();
}
