using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Piperf {
  public partial class MainWindow : Window, INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly Config _cfg;
    private readonly SensorReader _sensors = new();
    private readonly ThroughputCounters _counters = new();
    private readonly DispatcherTimer _timer;
    private bool _clickThrough = true; // default

    public double OverlayOpacity => _cfg.Opacity;
    public double FontSize => _cfg.FontSize;

    public MainWindow(Config cfg) {
      _cfg = cfg;
      DataContext = this;
      InitializeComponent();                 // <-- this compiles when XAML Build Action=Page and x:Class matches
      _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollIntervalMs) };
      _timer.Tick += (_, __) => Tick();
      _timer.Start();
    }

    void Window_Loaded(object sender, RoutedEventArgs e) {
      RestorePositionOrTopRight();
      UpdateClickThrough();
    }
    void Window_SourceInitialized(object? sender, EventArgs e) => UpdateClickThrough();

    private void RestorePositionOrTopRight() {
      if (_cfg.PositionX.HasValue && _cfg.PositionY.HasValue) {
        Left = _cfg.PositionX.Value;
        Top  = _cfg.PositionY.Value;
      } else {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 10;
        Top  = wa.Top + 10;
      }
    }

    private void SavePosition() {
      _cfg.PositionX = Left;
      _cfg.PositionY = Top;
      Config.Save(_cfg);
    }

    private void Tick() {
      try {
        var s = _sensors.Read();
        var t = _counters.Sample();
        string MBps(double bps) => (bps / (1024d * 1024d)).ToString("0.0") + " MB/s";

        OverlayText.Text =
          $"🖥️ CPU  {s.CpuLoad,3:0}%   🌡️ {FormatTemp(s.CpuTemp)}\n" +
          $"🎮 GPU  {s.GpuLoad,3:0}%   🌡️ {FormatTemp(s.GpuTemp)}\n" +
          $"💽 Disk  ⬇ R {MBps(t.DiskRead),7}   ⬆ W {MBps(t.DiskWrite)}\n" +
          $"📶 Net   ⬆ Up {MBps(t.NetUp),7}   ⬇ Down {MBps(t.NetDown)}";
      } catch {
        OverlayText.Text = "— sensors unavailable —";
      }
    }

    private static string FormatTemp(float value) {
      if (float.IsNaN(value) || value <= 0) return "—°C";
      return $"{value:0}°C";
    }

    // Alt + double-click toggles click-through
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && e.ClickCount == 2) {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
        e.Handled = true;
        return;
      }
      if (!_clickThrough && e.LeftButton == MouseButtonState.Pressed) {
        DragMove();
      }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
      if (!_clickThrough) SavePosition(); // persist new location after drag
    }

    // Optional backup hotkey
    private void Window_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.P && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                           (ModifierKeys.Control | ModifierKeys.Shift)) {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
      }
    }

    // Win32 styles
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void UpdateClickThrough() {
      SetHitTestable(!_clickThrough);
      Title = _clickThrough ? "Piperf (click-through)" : "Piperf (interactive)";
    }

    private void SetHitTestable(bool hitTestable) {
      var hwnd = new WindowInteropHelper(this).Handle;
      int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
      if (hitTestable) ex = (ex & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED;
      else ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
      SetWindowLong(hwnd, GWL_EXSTYLE, ex);
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
