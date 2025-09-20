using System;
using System.IO;
using System.Text.Json;

namespace Piperf {
  public sealed class Config {
    public const string DefaultPingTarget = "1.1.1.1";
    public const int DefaultPingIntervalMs = 500;

    public int    PollIntervalMs { get; set; } = 750;
    public double Opacity        { get; set; } = 0.9;
    public double FontSize       { get; set; } = 12.0;
    public string PingTarget     { get; set; } = DefaultPingTarget;
    public int    PingIntervalMs { get; set; } = DefaultPingIntervalMs;


    // Persisted window position
    public double? PositionX     { get; set; }
    public double? PositionY     { get; set; }

    public static class Paths {
      public static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Piperf");
      public static string ConfigDir  => Path.Combine(AppDataDir, "config");
      public static string ConfigFile => Path.Combine(ConfigDir, "config.json");
    }

    public static Config LoadOrDefault() {
      try {
        if (File.Exists(Paths.ConfigFile)) {
          var txt = File.ReadAllText(Paths.ConfigFile);
          var cfg = JsonSerializer.Deserialize<Config>(txt);
          if (cfg != null) return cfg;
        }
      } catch { /* ignore */ }

      var def = new Config();
      Save(def);
      return def;
    }

    public static void Save(Config cfg) {
      Directory.CreateDirectory(Paths.ConfigDir);
      var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(Paths.ConfigFile, json);
    }
  }
}
