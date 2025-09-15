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
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _inputTimer;   // NEW: fast Alt watcher

    private bool _clickThrough = true;
    private bool _currentHitTestable;               // tracks applied state

    public double OverlayOpacity => _cfg.Opacity;
    public double FontSize => _cfg.FontSize;

    public MainWindow(Config cfg) {
      _cfg = cfg;
      DataContext = this;
      InitializeComponent();

      // metrics update timer (as before)
      _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollIntervalMs) };
      _metricsTimer.Tick += (_, __) => TickMetrics();
      _metricsTimer.Start();

      // input watcher: ~30ms for instant Alt response
      _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
      _inputTimer.Tick += (_, __) => ApplyHitTestFromState();
      _inputTimer.Start();
    }

    void Window_Loaded(object sender, RoutedEventArgs e) {
      RestorePositionOrTopRight();
      UpdateClickThrough();     // sets baseline
      ApplyHitTestFromState();  // apply immediate state
    }
    void Window_SourceInitialized(object? sender, EventArgs e) {
      UpdateClickThrough();
      ApplyHitTestFromState();
    }

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

    // --- Metrics text update (unchanged except method name) ---
    private void TickMetrics() {
      try {
        var s = _sensors.Read();
        var t = _counters.Sample();
        string MBps(double bps) => (bps / (1024d * 1024d)).ToString("0.0") + " MB/s";

        OverlayText.Text =
          $"🖥️ CPU  {s.CpuLoad,3:0}%   🌡️ {FormatTemp(s.CpuTemp)}\n" +
          $"🎮 GPU  {s.GpuLoad,3:0}%   🌡️ {FormatTemp(s.GpuTemp)}\n" +
          $"💽 Disk  ⬆ W {MBps(t.DiskWrite),7}   ⬇ R {MBps(t.DiskRead)}\n" +
          $"📶 Net   ⬇ D {MBps(t.NetDown),7}   ⬆ U {MBps(t.NetDown)}";
      } catch {
        OverlayText.Text = "— sensors unavailable —";
      }
    }

    private static string FormatTemp(float value) => (float.IsNaN(value) || value <= 0) ? "—°C" : $"{value:0}°C";

    // --- Mouse / toggle logic ---
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

      // Alt + double-click => toggle persistent click-through mode
      if (alt && e.ClickCount == 2) {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();     // updates title + baseline
        ApplyHitTestFromState();  // ensure immediate effect
        e.Handled = true;
        return;
      }

      // Alt-drag to move (works in either mode, because ApplyHitTestFromState makes it hit-testable while Alt is down)
      if (e.LeftButton == MouseButtonState.Pressed && alt) {
        try { DragMove(); } catch { /* ignore */ }
      }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
      // Always persist final position after a drag
      SavePosition();
    }

    // Optional backup hotkey
    private void Window_KeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.P && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                           (ModifierKeys.Control | ModifierKeys.Shift)) {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
        ApplyHitTestFromState();
      }
    }

    // --- Hit-test management ---
    // Desired rule: window is hit-testable if (interactive mode) OR (Alt is currently held)
    private void ApplyHitTestFromState() {
      bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
      bool desiredHitTestable = !_clickThrough || alt;
      if (desiredHitTestable != _currentHitTestable) {
        SetHitTestable(desiredHitTestable);
        _currentHitTestable = desiredHitTestable;
      }
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void UpdateClickThrough() {
      Title = _clickThrough ? "Piperf (click-through)" : "Piperf (interactive)";
      // Do not set styles here directly—ApplyHitTestFromState() consolidates logic with Alt handling
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
      _metricsTimer.Stop();
      _inputTimer.Stop();
      _sensors.Dispose();
      _counters.Dispose();
    }

    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
}
