using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Crush.Viewer;

internal static class Program {

  private const string _SCREENSHOT_SWITCH = "--screenshot";
  private static readonly TimeSpan _SCREENSHOT_TIMEOUT = TimeSpan.FromSeconds(30);

  [STAThread]
  static int Main(string[] args) {
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

    var form = new MainForm();
    var screenshotPath = _GetScreenshotPath(args);
    if (screenshotPath != null)
      return _RunScreenshotCapture(form, screenshotPath);

    if (args.Length > 0)
      form.OpenFileOnLoad(args[0]);

    Application.Run(form);
    return 0;
  }

  private static string? _GetScreenshotPath(string[] args) {
    var index = Array.FindIndex(args, arg => string.Equals(arg, _SCREENSHOT_SWITCH, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
      return null;
    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
      throw new ArgumentException($"{_SCREENSHOT_SWITCH} requires an output path.", nameof(args));
    return Path.GetFullPath(args[index + 1]);
  }

  private static int _RunScreenshotCapture(MainForm form, string screenshotPath) {
    var fixturePath = Path.Combine(Path.GetTempPath(), $"pngcrushcs-viewer-{Guid.NewGuid():N}.png");
    _CreateScreenshotFixture(fixturePath);
    form.OpenFileOnLoad(fixturePath);

    var stopwatch = Stopwatch.StartNew();
    var exitCode = 1;
    using var timer = new System.Windows.Forms.Timer { Interval = 100 };

    form.Shown += (_, _) => timer.Start();
    timer.Tick += (_, _) => {
      if (stopwatch.Elapsed >= _SCREENSHOT_TIMEOUT) {
        timer.Stop();
        form.Close();
        return;
      }

      if (!form.Enabled || string.Equals(form.Text, "Crush Viewer", StringComparison.Ordinal))
        return;

      timer.Stop();
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        form.PerformLayout();
        using var screenshot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
        screenshot.Save(screenshotPath, ImageFormat.Png);
        exitCode = 0;
      } finally {
        form.Close();
      }
    };

    try {
      Application.Run(form);
      return exitCode;
    } finally {
      try { File.Delete(fixturePath); } catch (IOException) { }
      try { File.Delete(fixturePath + ".pal"); } catch (IOException) { }
    }
  }

  private static void _CreateScreenshotFixture(string path) {
    const int width = 720;
    const int height = 420;
    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

    using (var background = new LinearGradientBrush(
      new Rectangle(0, 0, width, height),
      Color.FromArgb(22, 31, 52),
      Color.FromArgb(76, 43, 100),
      28f
    ))
      graphics.FillRectangle(background, 0, 0, width, height);

    using (var glow = new SolidBrush(Color.FromArgb(70, 56, 189, 248)))
      graphics.FillEllipse(glow, 405, -105, 390, 390);
    using (var glow = new SolidBrush(Color.FromArgb(65, 244, 114, 182)))
      graphics.FillEllipse(glow, -135, 220, 360, 360);

    using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
    for (var x = 0; x < width; x += 40)
      graphics.DrawLine(gridPen, x, 0, x, height);
    for (var y = 0; y < height; y += 40)
      graphics.DrawLine(gridPen, 0, y, width, y);

    using var titleFont = new Font("Segoe UI", 38f, FontStyle.Bold, GraphicsUnit.Pixel);
    using var subtitleFont = new Font("Segoe UI", 20f, FontStyle.Regular, GraphicsUnit.Pixel);
    using var smallFont = new Font("Consolas", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    using var white = new SolidBrush(Color.White);
    using var muted = new SolidBrush(Color.FromArgb(215, 226, 238));
    using var accent = new SolidBrush(Color.FromArgb(126, 231, 255));

    graphics.DrawString("PNGCrushCS", titleFont, white, 56, 70);
    graphics.DrawString("Crush Viewer", subtitleFont, accent, 59, 126);
    graphics.DrawString("pure-managed image formats · inspect · transform · convert", smallFont, muted, 60, 178);

    var swatches = new[] {
      Color.FromArgb(255, 239, 91, 91),
      Color.FromArgb(255, 246, 189, 96),
      Color.FromArgb(255, 78, 205, 196),
      Color.FromArgb(255, 90, 160, 255),
      Color.FromArgb(255, 186, 104, 255),
    };
    for (var i = 0; i < swatches.Length; ++i) {
      using var brush = new SolidBrush(swatches[i]);
      graphics.FillRectangle(brush, new Rectangle(60 + i * 74, 245, 54, 54));
    }

    using var outline = new Pen(Color.FromArgb(180, 255, 255, 255), 2f);
    graphics.DrawRectangle(outline, new Rectangle(56, 236, 386, 72));
    graphics.DrawString("lossless fixture.png", smallFont, muted, 60, 334);

    bitmap.Save(path, ImageFormat.Png);
  }
}
