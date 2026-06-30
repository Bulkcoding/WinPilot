using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    // ── 단축키 캡처 ──

    private readonly List<Key> _captureBuffer = new();

    private void OnStartCaptureHotkey(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.IsCapturingHotkey) return;
        _captureBuffer.Clear();
        vm.IsCapturingHotkey = true;
        // Focus the capture border so it receives keyboard events
        CaptureBorder.Focusable = true;
        Keyboard.Focus(CaptureBorder);
        CaptureBorder.Focus();
        e.Handled = true;
    }

    private void OnCapturePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || !vm.IsCapturingHotkey) return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Skip modifier-only keys
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        // Esc to cancel
        if (key == Key.Escape)
        {
            _captureBuffer.Clear();
            vm.IsCapturingHotkey = false;
            return;
        }

        if (!_captureBuffer.Contains(key))
            _captureBuffer.Add(key);

        if (_captureBuffer.Count >= 2)
        {
            vm.SetHotkey(_captureBuffer[0], _captureBuffer[1]);
            _captureBuffer.Clear();
            vm.IsCapturingHotkey = false;
        }
    }
}
