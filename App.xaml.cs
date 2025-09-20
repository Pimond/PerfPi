using System.IO;
using System.Windows;

namespace Piperf {
  public partial class App : System.Windows.Application {
    protected override void OnStartup(StartupEventArgs e) {
      base.OnStartup(e);
      Directory.CreateDirectory(Config.Paths.ConfigDir);
      var cfg = Config.LoadOrDefault();
      var wnd = new MainWindow(cfg);
      wnd.Show();
    }
  }
}
