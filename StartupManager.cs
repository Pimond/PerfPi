using Microsoft.Win32;

namespace Piperf {
  public static class StartupManager {
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Piperf";

    public static bool IsEnabled() {
      using var rk = Registry.CurrentUser.OpenSubKey(RunKey, false);
      return rk?.GetValue(AppName) is string;
    }

    public static void Enable(string exePath) {
      using var rk = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? 
                     Registry.CurrentUser.CreateSubKey(RunKey);
      rk.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void Disable() {
      using var rk = Registry.CurrentUser.OpenSubKey(RunKey, true);
      rk?.DeleteValue(AppName, false);
    }
  }
}
