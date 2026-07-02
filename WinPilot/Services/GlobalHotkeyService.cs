using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WinPilot.Models;

namespace WinPilot.Services;

/// <summary>
/// Global hotkey service using a low-level keyboard hook (WH_KEYBOARD_LL).
/// Detects when two non-modifier keys are held simultaneously, then fires an event.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookProc;
    private readonly HashSet<int> _pressedKeys = new();
    private bool _comboActive;

    private int _key1; // virtual-key code
    private int _key2; // virtual-key code

    /// <summary>Fired when the configured two-key combination is pressed globally.</summary>
    public event Action? HotkeyTriggered;

    public GlobalHotkeyService()
    {
        _hookProc = HookCallback; // keep delegate alive
    }

    /// <summary>Update the hotkey combination from a HotkeySetting model.</summary>
    public void SetFromSetting(HotkeySetting setting)
    {
        _key1 = setting.Key1;
        _key2 = setting.Key2;
    }

    /// <summary>Start the global keyboard hook.</summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using var curProcess = Process.GetCurrentProcess();
        using var mainModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(mainModule?.ModuleName);

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
    }

    /// <summary>Stop the global keyboard hook.</summary>
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _pressedKeys.Clear();
        _comboActive = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            switch ((int)wParam)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    _pressedKeys.Add(vkCode);
                    CheckCombination();
                    break;

                case WM_KEYUP:
                case WM_SYSKEYUP:
                    _pressedKeys.Remove(vkCode);
                    if (!_pressedKeys.Contains(_key1) || !_pressedKeys.Contains(_key2))
                        _comboActive = false;
                    break;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void CheckCombination()
    {
        if (_comboActive)
            return;

        if (_pressedKeys.Contains(_key1) && _pressedKeys.Contains(_key2))
        {
            _comboActive = true;
            HotkeyTriggered?.Invoke();
        }
    }

    public void Dispose() => Stop();

    // ── Win32 ──

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
