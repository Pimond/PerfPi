using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
    private bool? _currentHitTestable;
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

      _pingDisplay = FormatPingDisplay(null, null);
      SetMetricsUnavailable();

      _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ResolvePingInterval()) };
      _pingTimer.Tick += PingTimer_Tick;
      _pingTimer.Start();

      _ = UpdatePingAsync();
    }

    public double OverlayOpacity => _cfg.Opacity;
    public double UiFontSize => _cfg.FontSize;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      RestorePositionOrTopRight();
      ApplyHitTestFromState();
      Dispatcher.InvokeAsync(StartMetricsInitialization, DispatcherPriority.Background);
      _tray?.RefreshMenu();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
      ApplyHitTestFromState();
    }

    private void StartMetricsInitialization()
    {
      if (_sensorInitTask != null) return;

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
        await Dispatcher.InvokeAsync(() => SetMetricsUnavailable("Metrics unavailable"));
      }
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

        CpuLoadText.Text = FormatPercentage(sensorSnapshot.CpuLoad);
        CpuTempText.Text = FormatTemp(sensorSnapshot.CpuTemp);

        GpuLoadText.Text = FormatPercentage(sensorSnapshot.GpuLoad);
        GpuTempText.Text = FormatTemp(sensorSnapshot.GpuTemp);

        DiskWriteText.Text = FormatThroughputSegment("W", throughputSnapshot.DiskWrite);
        DiskReadText.Text = FormatThroughputSegment("R", throughputSnapshot.DiskRead);

        NetDownText.Text = FormatThroughputSegment("D", throughputSnapshot.NetDown);
        NetUpText.Text = FormatThroughputSegment("U", throughputSnapshot.NetUp);

        PingValueText.Text = _pingDisplay;
        StatusText.Text = string.Empty;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Metrics update failed: {ex}");
        SetMetricsUnavailable("Metrics unavailable");
      }
    }

    private static string FormatThroughput(double bytesPerSecond)
    {
      if (double.IsNaN(bytesPerSecond))
        return $"{EmDash} MB/s";

      const double megabyte = 1024d * 1024d;
      var mbPerSecond = bytesPerSecond / megabyte;
      if (double.IsNaN(mbPerSecond) || double.IsInfinity(mbPerSecond) || mbPerSecond < 0)
        mbPerSecond = 0;

      return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB/s", mbPerSecond);
    }

    private static string FormatThroughputSegment(string label, double bytesPerSecond)
    {
      var metric = FormatThroughput(bytesPerSecond);
      return string.Format(CultureInfo.InvariantCulture, "{0} {1}", label, metric);
    }

    private static string FormatTemp(float value)
    {
      return (float.IsNaN(value) || value <= 0)
        ? EmDash
        : string.Format(CultureInfo.InvariantCulture, "{0:0}{1}", value, DegreeCelsius);
    }

    private static string FormatPercentage(float value)
    {
      return float.IsNaN(value)
        ? EmDash
        : string.Format(CultureInfo.InvariantCulture, "{0:0}%", value);
    }

    private void SetMetricsUnavailable(string? statusMessage = null)
    {
      CpuLoadText.Text = EmDash;
      CpuTempText.Text = EmDash;
      GpuLoadText.Text = EmDash;
      GpuTempText.Text = EmDash;
      DiskWriteText.Text = FormatThroughputSegment("W", double.NaN);
      DiskReadText.Text = FormatThroughputSegment("R", double.NaN);
      NetDownText.Text = FormatThroughputSegment("D", double.NaN);
      NetUpText.Text = FormatThroughputSegment("U", double.NaN);
      PingValueText.Text = _pingDisplay;
      StatusText.Text = string.IsNullOrWhiteSpace(statusMessage) ? "Loading data..." : statusMessage;
    }

    private int ResolvePingInterval()
    {
      var configured = _cfg.PingIntervalMs <= 0 ? Config.DefaultPingIntervalMs : _cfg.PingIntervalMs;
      if (configured < Config.DefaultPingIntervalMs) configured = Config.DefaultPingIntervalMs;
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

      string display = _pingDisplay;

      try
      {
        var target = string.IsNullOrWhiteSpace(_cfg.PingTarget) ? Config.DefaultPingTarget : _cfg.PingTarget;
        var timeout = ResolvePingInterval();

        try
        {
          var reply = await _ping.SendPingAsync(target, timeout).ConfigureAwait(false);
          display = reply.Status == IPStatus.Success
            ? FormatPingDisplay(reply.RoundtripTime, null)
            : FormatPingDisplay(null, null);
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Ping failed: {ex.Message}");
          display = FormatPingDisplay(null, null);
        }
      }
      finally
      {
        _pingInFlight = false;
      }

      _pingDisplay = display;
      await Dispatcher.InvokeAsync(() =>
      {
        if (!_isClosing)
        {
          PingValueText.Text = display;
        }
      });
    }

    private static string FormatPingDisplay(long? latencyMs, string? status)
    {
      if (latencyMs.HasValue)
      {
        return string.Format(CultureInfo.InvariantCulture, "{0,6:0} ms", latencyMs.Value);
      }

      _ = status;
      return EmDash;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
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

    private void ApplyHitTestFromState()
    {
      bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
      if (_currentHitTestable.HasValue && altPressed == _currentHitTestable.Value) return;

      SetHitTestable(altPressed);
      _currentHitTestable = altPressed;
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

    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private const string DegreeCelsius = "\u00B0C";
    private const string EmDash = "\u2014";

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
  }
}
