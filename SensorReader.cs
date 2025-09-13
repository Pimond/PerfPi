using System;
using LibreHardwareMonitor.Hardware;

namespace PerfPi {
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
      float cpuLoad = float.NaN, cpuTemp = float.NaN, gpuLoad = float.NaN, gpuTemp = float.NaN;

      _pc.Accept(new UpdateVisitor());
      foreach (var hw in _pc.Hardware) {
        hw.Update();

        if (hw.HardwareType == HardwareType.Cpu) {
          foreach (var s in hw.Sensors) {
            if (s.SensorType == SensorType.Load && (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase)))
              cpuLoad = s.Value ?? cpuLoad;
            if (s.SensorType == SensorType.Temperature && (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
              cpuTemp = s.Value ?? cpuTemp;
          }
        }

        if (hw.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel) {
          foreach (var s in hw.Sensors) {
            if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
              gpuLoad = s.Value ?? gpuLoad;
            if (s.SensorType == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
              gpuTemp = s.Value ?? gpuTemp;
          }
        }
      }

      // Graceful fallback to 0/NaN display handled by UI formatting
      return (cpuLoad, cpuTemp, gpuLoad, gpuTemp);
    }

    public void Dispose() => _pc.Close();
  }

  // Forces sensor tree updates
  internal sealed class UpdateVisitor : IVisitor {
    public void VisitComputer(IComputer computer) { computer.Traverse(this); }
    public void VisitHardware(IHardware hardware) { hardware.Update(); foreach (var sub in hardware.SubHardware) sub.Accept(this); }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
  }
}
