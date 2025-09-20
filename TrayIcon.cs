using System;
using System.Drawing;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Piperf
{
  public sealed class TrayIcon : IDisposable
  {
    private readonly WinForms.NotifyIcon _notifyIcon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly WinForms.ToolStripMenuItem _toggleItem = new();
    private readonly WinForms.ToolStripMenuItem _startupItem = new();
    private readonly MainWindow _window;

    public TrayIcon(MainWindow window)
    {
      _window = window;

      _toggleItem.Click += (_, __) =>
      {
        _window.ToggleClickThrough();
      };

      _startupItem.Click += (_, __) => ToggleStartup();

      _menu.Items.Add(new WinForms.ToolStripMenuItem("Show", null, (_, __) => ShowWindow()));
      _menu.Items.Add(_toggleItem);
      _menu.Items.Add(new WinForms.ToolStripSeparator());
      _menu.Items.Add(_startupItem);
      _menu.Items.Add(new WinForms.ToolStripSeparator());
      _menu.Items.Add(new WinForms.ToolStripMenuItem("Quit", null, (_, __) => Quit()));

      _notifyIcon.ContextMenuStrip = _menu;
      _notifyIcon.Text = "Piperf";
      _notifyIcon.Icon = LoadIcon();
      _notifyIcon.Visible = true;
      _notifyIcon.DoubleClick += (_, __) => ShowWindow();

      UpdateText();
    }

    public void UpdateText()
    {
      _toggleItem.Text = _window.IsClickThrough ? "Switch to interactive" : "Switch to click-through";
      _startupItem.Text = StartupManager.IsEnabled() ? "Disable Start with Windows" : "Enable Start with Windows";
    }

    private void ToggleStartup()
    {
      var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
      if (string.IsNullOrEmpty(exe)) return;

      if (StartupManager.IsEnabled())
      {
        StartupManager.Disable();
      }
      else
      {
        StartupManager.Enable(exe);
      }

      UpdateText();
    }

    private static Icon LoadIcon()
    {
      try
      {
        var resourceUri = new Uri("pack://application:,,,/Assets/Piperf.ico", UriKind.Absolute);
        var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
        if (streamInfo?.Stream != null)
        {
          using var resourceStream = streamInfo.Stream;
          return new Icon(resourceStream);
        }
      }
      catch
      {
        // fall through to disk fallback
      }

      var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Piperf.ico");
      if (File.Exists(candidate))
      {
        using var fileStream = File.OpenRead(candidate);
        return new Icon(fileStream);
      }

      return SystemIcons.Application;
    }

    private void ShowWindow()
    {
      if (_window.WindowState == WindowState.Minimized)
      {
        _window.WindowState = WindowState.Normal;
      }

      _window.Show();
      _window.Activate();
    }

    private void Quit()
    {
      _notifyIcon.Visible = false;
      System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
      _notifyIcon.Visible = false;
      _notifyIcon.Icon?.Dispose();
      _notifyIcon.Dispose();
      _menu.Dispose();
    }
  }
}
