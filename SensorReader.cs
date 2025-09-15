using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace Piperf {
  public sealed class SensorReader : IDisposable {
    private readonly Computer _pc;

    public SensorReader() {
      _pc = new Computer {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true
      };
      _pc.Open();
    }

    public (float CpuLoad, float CpuTemp, float GpuLoad, float GpuTemp) Read() {
      _pc.Accept(new UpdateVisitor());

      float cpuLoad = float.NaN, cpuTemp = float.NaN, gpuLoad = float.NaN, gpuTemp = float.NaN;

      foreach (var hw in _pc.Hardware) {
        if (hw.HardwareType == HardwareType.Cpu) {
          // Load
          foreach (var s in EnumerateSensors(hw).Where(s => s.SensorType == SensorType.Load)) {
            if (s.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Equals("Total", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
              cpuLoad = s.Value ?? cpuLoad;
          }

          // Temperature ranking: Package > Tctl/Tdie/Die > CPU > Core
          float? best = null; int bestRank = int.MaxValue;
          foreach (var s in EnumerateSensors(hw).Where(s => s.SensorType == SensorType.Temperature)) {
            var n = s.Name.ToLowerInvariant();
            int rank =
              n.Contains("package") ? 0 :
              (n.Contains("tctl") || n.Contains("tdie") || n.Contains("die")) ? 1 :
              n.Contains("cpu") ? 2 :
              n.Contains("core") ? 3 : 9;

            var val = s.Value ?? float.NaN;
            if (!float.IsNaN(val) && (best == null || rank < bestRank || (rank == bestRank && val > best.Value))) {
              best = val; bestRank = rank;
            }
          }
          if (best == null) {
            var coreMax = EnumerateSensors(hw)
              .Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
              .Select(s => s.Value ?? float.NaN)
              .Where(v => !float.IsNaN(v));
            if (coreMax.Any()) best = coreMax.Max();
          }
          if (best.HasValue) cpuTemp = best.Value;
        }

        if (hw.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel) {
          float? gBest = null; int gRank = int.MaxValue;
          foreach (var s in EnumerateSensors(hw)) {
            if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
              gpuLoad = s.Value ?? gpuLoad;

            if (s.SensorType == SensorType.Temperature) {
              var n = s.Name.ToLowerInvariant();
              int rank = n.Contains("core") ? 0 : n.Contains("edge") ? 1 : 5;
              var val = s.Value ?? float.NaN;
              if (!float.IsNaN(val) && (gBest == null || rank < gRank)) { gBest = val; gRank = rank; }
            }
          }
          if (gBest.HasValue) gpuTemp = gBest.Value;
        }
      }

      return (cpuLoad, cpuTemp, gpuLoad, gpuTemp);
    }

    // Recursively enumerate sensors on hardware and sub-hardware
    private static IEnumerable<ISensor> EnumerateSensors(IHardware hw) {
      foreach (var s in hw.Sensors) yield return s;
      foreach (var sub in hw.SubHardware) {
        foreach (var s in EnumerateSensors(sub)) yield return s;
      }
    }

    public void Dispose() => _pc.Close();
  }

  internal sealed class UpdateVisitor : IVisitor {
    public void VisitComputer(IComputer computer) { computer.Traverse(this); }
    public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var sub in hardware.SubHardware) sub.Accept(this); }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
  }
}
