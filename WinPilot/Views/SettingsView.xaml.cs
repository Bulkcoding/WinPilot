using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WinPilot.ViewModels;

namespace WinPilot.Views;

public partial class SettingsView : UserControl
{
    // 프로그램적으로 Password를 채울 때 PasswordChanged가 다시 VM을 건드리지 않도록 가드
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        SetBoxPassword(vm.DeepSeekApiKey);            // 저장된 키를 ●●● 로 표시
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    // 삭제 등 외부에서 VM 값이 바뀌면 박스도 동기화 (값이 다를 때만 → 입력 루프 방지)
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.DeepSeekApiKey)) return;
        if (DataContext is SettingsViewModel vm && vm.DeepSeekApiKey != DeepSeekPwdBox.Password)
            SetBoxPassword(vm.DeepSeekApiKey);
    }

    // 사용자가 입력하면 VM으로 반영
    private void OnDeepSeekPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (DataContext is SettingsViewModel vm)
            vm.DeepSeekApiKey = DeepSeekPwdBox.Password;
    }

    private void SetBoxPassword(string value)
    {
        _syncing = true;
        DeepSeekPwdBox.Password = value ?? "";
        _syncing = false;
    }
}
