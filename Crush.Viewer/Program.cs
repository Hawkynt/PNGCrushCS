using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using FileFormat.Core;
using ImageRegistry = Hawkynt.FileFormats.Images.FormatRegistry;

namespace Crush.Viewer;

internal static class Program {

  [STAThread]
  public static void Main(string[] args)
    => AppBuilder.Configure<Application>()
      .UsePlatformDetect()
      .WithInterFont()
      .Start(_Run, args);

  private static void _Run(Application app, string[] args) {
    app.Styles.Add(new FluentTheme());

    var options = ViewerLaunchOptions.Parse(args);
    string? fixturePath = null;
    try {
      if (options.ScreenshotPath != null && options.InitialPath == null) {
        fixturePath = _CreateScreenshotFixture();
        options = options with { InitialPath = fixturePath };
      }

      var window = new MainWindow(options);
      window.Show();
      app.Run(window);
    } finally {
      if (fixturePath != null) {
        try { File.Delete(fixturePath); } catch { }
      }
    }
  }

  private static string _CreateScreenshotFixture() {
    const int width = 960;
    const int height = 540;
    var pixels = new byte[width * height * 4];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var i = (y * width + x) * 4;
        var tile = ((x / 80) + (y / 80)) & 1;
        var fx = x / (double)(width - 1);
        var fy = y / (double)(height - 1);
        pixels[i] = (byte)(35 + 150 * fy + tile * 18);
        pixels[i + 1] = (byte)(45 + 145 * fx);
        pixels[i + 2] = (byte)(160 + 70 * (1 - fy));
        pixels[i + 3] = 255;
      }

    var image = new RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = pixels,
    };

    var path = Path.Combine(Path.GetTempPath(), $"pngcrushcs-viewer-{Guid.NewGuid():N}.png");
    if (!ImageRegistry.Write(image, ImageFormat.Png, new FileInfo(path)))
      throw new InvalidOperationException("PNG writer could not create the viewer screenshot fixture.");
    return path;
  }
}

internal sealed record ViewerLaunchOptions(string? InitialPath, string? ScreenshotPath, bool SmokeTest) {

  internal static ViewerLaunchOptions Parse(string[] args) {
    string? initialPath = null;
    string? screenshotPath = null;
    var smokeTest = false;

    for (var i = 0; i < args.Length; ++i) {
      switch (args[i]) {
        case "--screenshot" when i + 1 < args.Length:
          screenshotPath = Path.GetFullPath(args[++i]);
          break;
        case "--smoke-test":
          smokeTest = true;
          break;
        case "--open" when i + 1 < args.Length:
          initialPath = Path.GetFullPath(args[++i]);
          break;
        default:
          if (!args[i].StartsWith('-', StringComparison.Ordinal) && initialPath == null)
            initialPath = Path.GetFullPath(args[i]);
          break;
      }
    }

    return new(initialPath, screenshotPath, smokeTest);
  }
}
