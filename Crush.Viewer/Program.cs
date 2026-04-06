using System;
using System.Windows.Forms;

namespace Crush.Viewer;

internal static class Program {

  [STAThread]
  static void Main(string[] args) {
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

    var form = new MainForm();
    if (args.Length > 0)
      form.OpenFileOnLoad(args[0]);

    Application.Run(form);
  }
}
