using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Piperf
{
  public sealed class ThroughputCounters : IDisposable
  {
    private readonly PerformanceCounter _diskRead;
    private readonly PerformanceCounter _diskWrite;

    private readonly List<PerformanceCounter> _netRecvAll = new();
    private readonly List<PerformanceCounter> _netSendAll = new();

    private PerformanceCounter? _netRecvPrimary;
    private PerformanceCounter? _netSendPrimary;

    private DateTime _lastPrimaryRefresh = DateTime.MinValue;

    public ThroughputCounters()
    {
      _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
      _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

      var category = new PerformanceCounterCategory("Network Interface");
      foreach (var instance in category.GetInstanceNames())
      {
        try
        {
          _netRecvAll.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", instance));
          _netSendAll.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance));
        }
        catch
        {
          // ignore counters we cannot open
        }
      }

      PickPrimaryInterface();

      // Prime counters to avoid initial zero burst
      TryPrime(_diskRead);
      TryPrime(_diskWrite);
      _netRecvAll.ForEach(TryPrime);
      _netSendAll.ForEach(TryPrime);
    }

    public (double DiskRead, double DiskWrite, double NetUp, double NetDown) Sample()
    {
      if ((DateTime.UtcNow - _lastPrimaryRefresh).TotalSeconds > 10)
      {
        PickPrimaryInterface();
      }

      double diskRead = SafeNext(_diskRead);
      double diskWrite = SafeNext(_diskWrite);

      double netDown = _netRecvPrimary != null ? SafeNext(_netRecvPrimary) : 0;
      double netUp = _netSendPrimary != null ? SafeNext(_netSendPrimary) : 0;

      if (netDown <= 0 && netUp <= 0)
      {
        netDown = _netRecvAll.Count > 0 ? _netRecvAll.Max(SafeNext) : 0;
        netUp = _netSendAll.Count > 0 ? _netSendAll.Max(SafeNext) : 0;
      }

      return (diskRead, diskWrite, netUp, netDown);
    }

    private static void TryPrime(PerformanceCounter counter)
    {
      try { _ = counter.NextValue(); }
      catch { }
    }

    private static double SafeNext(PerformanceCounter counter)
    {
      try { return counter.NextValue(); }
      catch { return 0; }
    }

    private void PickPrimaryInterface()
    {
      _lastPrimaryRefresh = DateTime.UtcNow;
      DisposePrimaryCounters();

      try
      {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
          .Where(n => n.OperationalStatus == OperationalStatus.Up)
          .ToList();

        if (nics.Count == 0)
        {
          _netRecvPrimary = null;
          _netSendPrimary = null;
          return;
        }

        var preferred = nics
          .Where(n => HasDefaultGateway(n))
          .DefaultIfEmpty(nics.First())
          .First();

        var category = new PerformanceCounterCategory("Network Interface");
        string? instance = category.GetInstanceNames()
          .FirstOrDefault(name =>
            name.IndexOf(preferred.Description, StringComparison.OrdinalIgnoreCase) >= 0 ||
            preferred.Description.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

        if (instance == null)
        {
          _netRecvPrimary = null;
          _netSendPrimary = null;
          return;
        }

        _netRecvPrimary = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
        _netSendPrimary = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);

        TryPrime(_netRecvPrimary);
        TryPrime(_netSendPrimary);
      }
      catch
      {
        _netRecvPrimary = null;
        _netSendPrimary = null;
      }
    }

    private static bool HasDefaultGateway(NetworkInterface nic)
    {
      try
      {
        return nic.GetIPProperties().GatewayAddresses.Any(g =>
        {
          if (g?.Address == null) return false;
          return !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any);
        });
      }
      catch
      {
        return false;
      }
    }

    private void DisposePrimaryCounters()
    {
      _netRecvPrimary?.Dispose();
      _netSendPrimary?.Dispose();
      _netRecvPrimary = null;
      _netSendPrimary = null;
    }

    public void Dispose()
    {
      _diskRead.Dispose();
      _diskWrite.Dispose();
      DisposePrimaryCounters();

      foreach (var counter in _netRecvAll) counter?.Dispose();
      foreach (var counter in _netSendAll) counter?.Dispose();
    }
  }
}
