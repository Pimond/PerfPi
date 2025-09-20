using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace Piperf
{
  public sealed class SensorReader : IDisposable
  {
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();

    public SensorReader()
    {
      _computer = new Computer
      {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true
      };

      _computer.Open();
    }

    public (float CpuLoad, float CpuTemp, float GpuLoad, float GpuTemp) Read()
    {
      _computer.Accept(_visitor);

      float cpuLoad = float.NaN;
      float gpuLoad = float.NaN;
      float? cpuTemp = null;
      int cpuTempRank = int.MaxValue;
      float? gpuTemp = null;
      int gpuTempRank = int.MaxValue;

      foreach (var hardware in _computer.Hardware)
      {
        switch (hardware.HardwareType)
        {
          case HardwareType.Cpu:
            ProcessCpuSensors(hardware, ref cpuLoad, ref cpuTemp, ref cpuTempRank);
            break;

          case HardwareType.GpuAmd:
          case HardwareType.GpuNvidia:
          case HardwareType.GpuIntel:
            ProcessGpuSensors(hardware, ref gpuLoad, ref gpuTemp, ref gpuTempRank);
            break;
        }
      }

      if (!cpuTemp.HasValue)
      {
        var fallback = _computer.Hardware
          .Where(h => h.HardwareType == HardwareType.Motherboard)
          .SelectMany(EnumerateSensors)
          .Where(s => s.SensorType == SensorType.Temperature &&
                      (s.Name.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                       s.Name.Contains("package", StringComparison.OrdinalIgnoreCase)))
          .Select(s => s.Value ?? float.NaN)
          .Where(v => !float.IsNaN(v));

        if (fallback.Any()) cpuTemp = fallback.Max();
      }

      return (cpuLoad, cpuTemp ?? float.NaN, gpuLoad, gpuTemp ?? float.NaN);
    }

    private static void ProcessCpuSensors(IHardware hardware, ref float load, ref float? temperature, ref int tempRank)
    {
      var sensors = EnumerateSensors(hardware).ToList();

      foreach (var sensor in sensors)
      {
        if (sensor.SensorType == SensorType.Load && LooksLikeCpuLoad(sensor.Name))
        {
          var value = sensor.Value;
          if (value.HasValue) load = value.Value;
        }

        if (sensor.SensorType == SensorType.Temperature)
        {
          var value = sensor.Value ?? float.NaN;
          if (float.IsNaN(value)) continue;

          int rank = RankCpuTemperature(sensor.Name);
          if (rank < tempRank || (rank == tempRank && (!temperature.HasValue || value > temperature.Value)))
          {
            temperature = value;
            tempRank = rank;
          }
        }
      }

      if (!temperature.HasValue)
      {
        var coreTemps = sensors
          .Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
          .Select(s => s.Value ?? float.NaN)
          .Where(v => !float.IsNaN(v));

        if (coreTemps.Any()) temperature = coreTemps.Max();
      }
    }

    private static void ProcessGpuSensors(IHardware hardware, ref float load, ref float? temperature, ref int tempRank)
    {
      var sensors = EnumerateSensors(hardware).ToList();

      foreach (var sensor in sensors)
      {
        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
        {
          var value = sensor.Value;
          if (value.HasValue) load = value.Value;
        }

        if (sensor.SensorType == SensorType.Temperature)
        {
          var value = sensor.Value ?? float.NaN;
          if (float.IsNaN(value)) continue;

          int rank = RankGpuTemperature(sensor.Name);
          if (rank < tempRank || (rank == tempRank && (!temperature.HasValue || value > temperature.Value)))
          {
            temperature = value;
            tempRank = rank;
          }
        }
      }
    }

    private static bool LooksLikeCpuLoad(string name)
    {
      return name.Contains("cpu total", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("total", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("total", StringComparison.OrdinalIgnoreCase);
    }

    private static int RankCpuTemperature(string name)
    {
      var lower = name.ToLowerInvariant();
      if (lower.Contains("package")) return 0;
      if (lower.Contains("tctl") || lower.Contains("tdie") || lower.Contains("die")) return 1;
      if (lower.Contains("cpu")) return 2;
      if (lower.Contains("core")) return 3;
      return 9;
    }

    private static int RankGpuTemperature(string name)
    {
      var lower = name.ToLowerInvariant();
      if (lower.Contains("core")) return 0;
      if (lower.Contains("edge")) return 1;
      return 5;
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
      foreach (var sensor in hardware.Sensors)
      {
        yield return sensor;
      }

      foreach (var sub in hardware.SubHardware)
      {
        foreach (var sensor in EnumerateSensors(sub))
        {
          yield return sensor;
        }
      }
    }

    public void Dispose()
    {
      _computer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
      public void VisitComputer(IComputer computer) => computer.Traverse(this);

      public void VisitHardware(IHardware hardware)
      {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
          sub.Accept(this);
        }
      }

      public void VisitSensor(ISensor sensor) { }
      public void VisitParameter(IParameter parameter) { }
    }
  }
}
