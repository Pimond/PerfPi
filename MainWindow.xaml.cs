using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Piperf
{
  public partial class MainWindow : Window, INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly Config _cfg;
    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _inputTimer;
    private readonly DispatcherTimer _pingTimer;
    private readonly Ping _ping = new();


    private Task<SensorReader>? _sensorInitTask;
    private Task<ThroughputCounters>? _counterInitTask;

    private SensorReader? _sensors;
    private ThroughputCounters? _counters;
    private bool _isClosing;
    private bool _clickThrough = true;
    private bool _currentHitTestable;
    private bool _pingInFlight;
    private string _pingDisplay = string.Empty;


    private TrayIcon? _tray;

    public MainWindow(Config cfg)
    {
      _cfg = cfg;
      DataContext = this;
      InitializeComponent();

      _tray = new TrayIcon(this);

      _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_cfg.PollIntervalMs) };
      _metricsTimer.Tick += (_, __) => TickMetrics();

      _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
      _inputTimer.Tick += (_, __) => ApplyHitTestFromState();
      _inputTimer.Start();

      _pingDisplay = FormatPingDisplay(null, "--");
      _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ResolvePingInterval()) };
      _pingTimer.Tick += PingTimer_Tick;
      _pingTimer.Start();

      _ = UpdatePingAsync();
    }

    public double OverlayOpacity => _cfg.Opacity;
    public double UiFontSize => _cfg.FontSize;
    public bool IsClickThrough => _clickThrough;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      RestorePositionOrTopRight();
      UpdateClickThrough();
      ApplyHitTestFromState();
      Dispatcher.InvokeAsync(StartMetricsInitialization, DispatcherPriority.Background);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
      UpdateClickThrough();
      ApplyHitTestFromState();
    }

    private void StartMetricsInitialization()
    {
      if (_sensorInitTask != null) return;

      OverlayText.Text = "Loading metrics...";

      _sensorInitTask = Task.Factory.StartNew(() => new SensorReader(),
                                              CancellationToken.None,
                                              TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                                              TaskScheduler.Default);
      _counterInitTask = Task.Run(() => new ThroughputCounters());

      _ = FinalizeMetricsInitializationAsync();
    }

    private async Task FinalizeMetricsInitializationAsync()
    {
      try
      {
        await Task.WhenAll(_sensorInitTask!, _counterInitTask!).ConfigureAwait(false);
        if (_isClosing) return;

        var sensors = _sensorInitTask!.Result;
        var counters = _counterInitTask!.Result;

        await Dispatcher.InvokeAsync(() =>
        {
          _sensors = sensors;
          _counters = counters;
          TickMetrics();
          _metricsTimer.Start();
        });
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Metrics initialization failed: {ex}");
        await Dispatcher.InvokeAsync(() => OverlayText.Text = "Metrics unavailable");
      }
    }

    public void ToggleClickThrough()
    {
      _clickThrough = !_clickThrough;
      UpdateClickThrough();
      ApplyHitTestFromState();
    }

    private void RestorePositionOrTopRight()
    {
      if (_cfg.PositionX.HasValue && _cfg.PositionY.HasValue)
      {
        Left = _cfg.PositionX.Value;
        Top = _cfg.PositionY.Value;
      }
      else
      {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 10;
        Top = wa.Top + 10;
      }
    }

    private void SavePosition()
    {
      _cfg.PositionX = Left;
      _cfg.PositionY = Top;
      Config.Save(_cfg);
    }

    private void TickMetrics()
    {
      var sensors = _sensors;
      var counters = _counters;

      if (sensors == null && _sensorInitTask is { IsCompletedSuccessfully: true } sensorTask)
      {
        sensors = sensorTask.Result;
        _sensors = sensors;
      }

      if (counters == null && _counterInitTask is { IsCompletedSuccessfully: true } counterTask)
      {
        counters = counterTask.Result;
        _counters = counters;
      }

      if (sensors == null || counters == null) return;

      try
      {
        var sensorSnapshot = sensors.Read();
        var throughputSnapshot = counters.Sample();

        var sb = new StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0,-4} {1,5:0}%  {2}", "CPU", sensorSnapshot.CpuLoad, FormatTemp(sensorSnapshot.CpuTemp))
          .AppendLine();

        sb.AppendFormat(CultureInfo.InvariantCulture, "{0,-4} {1,5:0}%  {2}", "GPU", sensorSnapshot.GpuLoad, FormatTemp(sensorSnapshot.GpuTemp))
          .AppendLine();

        sb.AppendFormat(CultureInfo.InvariantCulture, "{0,-4} W {1}  R {2}", "Disk", FormatThroughput(throughputSnapshot.DiskWrite), FormatThroughput(throughputSnapshot.DiskRead))
          .AppendLine();

        sb.AppendFormat(CultureInfo.InvariantCulture, "{0,-4} D {1}  U {2}", "Net", FormatThroughput(throughputSnapshot.NetDown), FormatThroughput(throughputSnapshot.NetUp))
          .AppendLine();

        sb.Append(_pingDisplay);

        OverlayText.Text = sb.ToString();
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Metrics update failed: {ex}");
        OverlayText.Text = "Metrics unavailable";
      }
    }

    private static string FormatThroughput(double bytesPerSecond)
    {
      const double megabyte = 1024d * 1024d;
      var mbPerSecond = bytesPerSecond / megabyte;
      if (mbPerSecond < 0) mbPerSecond = 0;
      return string.Format(CultureInfo.InvariantCulture, "{0,6:0.0} MB/s", mbPerSecond);
    }

    private static string FormatTemp(float value)
    {
      if (float.IsNaN(value) || value <= 0) return "  N/A";
      return string.Format(CultureInfo.InvariantCulture, "{0,4:0}{1}", value, DegreeCelsius);
    }

    private int ResolvePingInterval()
    {
      var configured = _cfg.PingIntervalMs <= 0 ? Config.DefaultPingIntervalMs : _cfg.PingIntervalMs;
      if (configured < 100) configured = 100;
      return configured;
    }

    private async void PingTimer_Tick(object? sender, EventArgs e)
    {
      await UpdatePingAsync();
    }

    private async Task UpdatePingAsync()
    {
      if (_pingInFlight) return;
      _pingInFlight = true;

      try
      {
        var target = string.IsNullOrWhiteSpace(_cfg.PingTarget) ? Config.DefaultPingTarget : _cfg.PingTarget;
        var timeout = ResolvePingInterval();

        try
        {
          var reply = await _ping.SendPingAsync(target, timeout).ConfigureAwait(false);
          _pingDisplay = reply.Status switch
          {
            IPStatus.Success => FormatPingDisplay(reply.RoundtripTime, null),
            IPStatus.TimedOut => FormatPingDisplay(null, "Timeout"),
            _ => FormatPingDisplay(null, reply.Status.ToString())
          };
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Ping failed: {ex.Message}");
          _pingDisplay = FormatPingDisplay(null, "Error");
        }
      }
      finally
      {
        _pingInFlight = false;
      }
    }

    private static string FormatPingDisplay(long? latencyMs, string? status)
    {
      if (latencyMs.HasValue)
      {
        return string.Format(CultureInfo.InvariantCulture, "{0,-4} {1,6:0} ms", "Ping", latencyMs.Value);
      }

      var display = status ?? "--";
      if (display.Length > 12) display = display.Substring(0, 12);
      return string.Format(CultureInfo.InvariantCulture, "{0,-4} {1}", "Ping", display);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

      if (altPressed && e.ClickCount == 2)
      {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
        ApplyHitTestFromState();
        e.Handled = true;
        return;
      }

      if (altPressed && e.LeftButton == MouseButtonState.Pressed)
      {
        try { DragMove(); }
        catch { }
      }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
      SavePosition();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == Key.P && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
      {
        _clickThrough = !_clickThrough;
        UpdateClickThrough();
        ApplyHitTestFromState();
      }
    }

    private void ApplyHitTestFromState()
    {
      bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
      bool desiredHitTestable = !_clickThrough || altPressed;

      if (desiredHitTestable == _currentHitTestable) return;

      SetHitTestable(desiredHitTestable);
      _currentHitTestable = desiredHitTestable;
    }

    private void UpdateClickThrough()
    {
      Title = _clickThrough ? "Piperf (click-through)" : "Piperf (interactive)";
      _tray?.UpdateText();
    }

    private void SetHitTestable(bool hitTestable)
    {
      var hwnd = new WindowInteropHelper(this).Handle;
      int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

      if (hitTestable)
      {
        exStyle = (exStyle & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED;
      }
      else
      {
        exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
      }

      SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    protected override void OnClosed(EventArgs e)
    {
      _isClosing = true;

      _metricsTimer.Stop();
      _inputTimer.Stop();
      _pingTimer.Stop();

      DisposeSensorReader();
      DisposeThroughputCounters();

      _tray?.Dispose();
      _tray = null;
      _ping.Dispose();

      base.OnClosed(e);
    }

    private void DisposeSensorReader()
    {
      try
      {
        if (_sensors != null)
        {
          _sensors.Dispose();
          _sensors = null;
          return;
        }

        var task = _sensorInitTask;
        if (task == null) return;

        if (task.IsCompletedSuccessfully)
        {
          task.Result.Dispose();
        }
        else if (!task.IsCompleted)
        {
          task.ContinueWith(t =>
          {
            if (t.IsCompletedSuccessfully) t.Result.Dispose();
          }, TaskScheduler.Default);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"SensorReader dispose failed: {ex}");
      }
    }

    private void DisposeThroughputCounters()
    {
      try
      {
        if (_counters != null)
        {
          _counters.Dispose();
          _counters = null;
          return;
        }

        var task = _counterInitTask;
        if (task == null) return;

        if (task.IsCompletedSuccessfully)
        {
          task.Result.Dispose();
        }
        else if (!task.IsCompleted)
        {
          task.ContinueWith(t =>
          {
            if (t.IsCompletedSuccessfully) t.Result.Dispose();
          }, TaskScheduler.Default);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"ThroughputCounters dispose failed: {ex}");
      }
    }

    private void SavePositionIfNeeded()
    {
      SavePosition();
    }

    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private const string DegreeCelsius = "\u00B0C";

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
  }
}




