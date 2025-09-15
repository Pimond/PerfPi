using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Piperf {
  public sealed class ThroughputCounters : IDisposable {
    private readonly PerformanceCounter _diskRead;
    private readonly PerformanceCounter _diskWrite;

    private readonly List<PerformanceCounter> _netRecvAll = new();
    private readonly List<PerformanceCounter> _netSendAll = new();
    private PerformanceCounter? _netRecvPrimary;
    private PerformanceCounter? _netSendPrimary;

    private DateTime _lastPrimaryPick = DateTime.MinValue;

    public ThroughputCounters() {
      // Disk (_Total)
      _diskRead  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total");
      _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

      // Network: prepare all instances; we’ll pick "primary" by default route mapping, with fallback to busiest
      var cat = new PerformanceCounterCategory("Network Interface");
      foreach (var inst in cat.GetInstanceNames()) {
        try {
          _netRecvAll.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", inst));
          _netSendAll.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec",     inst));
        } catch { /* skip bad instances */ }
      }
      PickPrimaryInterface();
      // Prime counters
      _ = _diskRead.NextValue(); _ = _diskWrite.NextValue();
      _netRecvAll.ForEach(c => { try { _ = c.NextValue(); } catch {} });
      _netSendAll.ForEach(c => { try { _ = c.NextValue(); } catch {} });
    }

    public (double DiskRead, double DiskWrite, double NetUp, double NetDown) Sample() {
      if ((DateTime.UtcNow - _lastPrimaryPick).TotalSeconds > 10) PickPrimaryInterface();

      double diskR = SafeNext(_diskRead);
      double diskW = SafeNext(_diskWrite);

      double netDown = _netRecvPrimary != null ? SafeNext(_netRecvPrimary) : 0;
      double netUp   = _netSendPrimary != null ? SafeNext(_netSendPrimary) : 0;

      // If primary mapping failed or is idle, fall back to busiest interface in this tick
      if (netDown <= 0 && netUp <= 0) {
        netDown = _netRecvAll.Select(SafeNext).DefaultIfEmpty(0).Max();
        netUp   = _netSendAll.Select(SafeNext).DefaultIfEmpty(0).Max();
      }

      return (diskR, diskW, netUp, netDown);
    }

    private static double SafeNext(PerformanceCounter c) {
      try { return c.NextValue(); } catch { return 0; }
    }

    private void PickPrimaryInterface() {
      _lastPrimaryPick = DateTime.UtcNow;
      try {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
          .Where(n => n.OperationalStatus == OperationalStatus.Up)
          .ToList();

        // Prefer NICs with a default gateway
        var gwNics = nics.Where(n => {
          try { return n.GetIPProperties().GatewayAddresses.Any(g => g?.Address != null && !g.Address.ToString().Equals("0.0.0.0")); }
          catch { return false; }
        }).ToList();

        var preferred = gwNics.FirstOrDefault() ?? nics.FirstOrDefault();
        if (preferred == null) { _netRecvPrimary = _netSendPrimary = null; return; }

        // Map NIC Description to perf counter instance name (best-effort fuzzy match)
        var cat = new PerformanceCounterCategory("Network Interface");
        string? inst = cat.GetInstanceNames().FirstOrDefault(i =>
          i.IndexOf(preferred.Description, StringComparison.OrdinalIgnoreCase) >= 0 ||
          preferred.Description.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0);

        if (inst != null) {
          _netRecvPrimary = new PerformanceCounter("Network Interface", "Bytes Received/sec", inst);
          _netSendPrimary = new PerformanceCounter("Network Interface", "Bytes Sent/sec",     inst);
          // Prime
          _ = _netRecvPrimary.NextValue(); _ = _netSendPrimary.NextValue();
          return;
        }

        // Fallback: leave null; Sample() will use busiest instance
        _netRecvPrimary = _netSendPrimary = null;
      } catch {
        _netRecvPrimary = _netSendPrimary = null;
      }
    }

    public void Dispose() {
      _diskRead?.Dispose(); _diskWrite?.Dispose();
      _netRecvPrimary?.Dispose(); _netSendPrimary?.Dispose();
      _netRecvAll.ForEach(c => c?.Dispose());
      _netSendAll.ForEach(c => c?.Dispose());
    }
  }
}
