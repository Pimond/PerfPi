using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PerfPi {
  public partial class MainWindow : Window, INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly Config _cfg;
    private readonly SensorReader _sensors = new();
    private readonly ThroughputCounters _counters = new();
    private readonly DispatcherTimer _timer;
    private bool _clickThrough = true;

    public double OverlayOpacity => _cfg.Opacity;
    public double FontSize => _cfg.FontSize;

    public MainWindow(Config cfg) {
      _cfg = cfg;
      DataContext = this;
      InitializeComponent();
      _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollIntervalMs) };
      _timer.Tick += (_, __) => Tick();
      _timer.Start();
    }

    void Window_Loaded(object sender, RoutedEventArgs e) {
      PositionTopRight();
      UpdateClickThrough();
    }

    void Window_SourceInitialized(object? sender, EventArgs e) => UpdateClickThrough();

    private void PositionTopRight() {
      var scr = SystemParameters.WorkArea; // primary screen work area
      Left = scr.Right - Width - 10;
      Top = scr.Top + 10;
    }

    private void Tick() {
      try {
        var s = _sensors.Read();
        var t = _counters.Sample();
        OverlayText.Text =
          $"CPU {s.CpuLoad,3:0}%  {s.CpuTemp,3:0}°C\n" +
          $"GPU {s.GpuLoad,3:0}%  {s.GpuTemp,3:0}°C\n" +
          $"Disk {FormatBps(t.DiskRead)}/{FormatBps(t.DiskWrite)}\n" +
          $"Net  {FormatBps(t.NetUp)}/{FormatBps(t.NetDown)}";
      } catch {
        OverlayText.Text = "— sensors unavailable —";
      }
    }

    private static string FormatBps(double v) {
      string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
      int i = 0; while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
      return $"{v:0.0}{units[i]}";
    }

    // --- Click-through toggle (Ctrl+Shift+P) ---
    private void Window_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
      }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
      if (!_clickThrough && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // --- Win32 styles for click-through ---
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void UpdateClickThrough() {
      var hwnd = new WindowInteropHelper(this).Handle;
      int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
      if (_clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
      else ex = (ex & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED;
      SetWindowLong(hwnd, GWL_EXSTYLE, ex);
      Title = _clickThrough ? "PerfPi (click-through)" : "PerfPi (interactive)";
    }

    protected override void OnClosed(EventArgs e) {
      base.OnClosed(e);
      _timer.Stop();
      _sensors.Dispose();
      _counters.Dispose();
    }

    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
}
