using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;

namespace WinPilot.Services;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private Window? _target;

    public TrayService()
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "WinPilot",
            Visible = false,
        };
        _icon.Click += (_, e) =>
        {
            if (e is System.Windows.Forms.MouseEventArgs me && me.Button == System.Windows.Forms.MouseButtons.Left)
                ShowWindow();
        };
        _icon.DoubleClick += (_, _) => ShowWindow();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowWindow());
        menu.Items.Add("종료", null, (_, _) => ExitApp());
        _icon.ContextMenuStrip = menu;
    }

    public void Attach(Window window)
    {
        _target = window;
        _target.Closing += OnWindowClosing;

        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
                _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            // 실패 시 기본 아이콘
        }

        _icon.Icon ??= System.Drawing.SystemIcons.Application;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        if (_target == null) return;
        _target.Hide();
        _icon.Visible = true;
    }

    private void ShowWindow()
    {
        if (_target == null) return;
        _icon.Visible = false;
        _target.Show();
        _target.WindowState = WindowState.Normal;
        _target.Activate();
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        if (_target != null)
        {
            _target.Closing -= OnWindowClosing;
            _target.Close();
        }
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
