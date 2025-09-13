using System;
using System.IO;
using System.Text.Json;

namespace PerfPi {
  public sealed class Config {
    public int PollIntervalMs { get; set; } = 750;
    public double Opacity { get; set; } = 0.9;
    public double FontSize { get; set; } = 12.0;

    public static class Paths {
      public static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PerfPi");
      public static string ConfigDir => Path.Combine(AppDataDir, "config");
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
      Directory.CreateDirectory(Paths.ConfigDir);
      File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
      return def;
    }
  }
}
