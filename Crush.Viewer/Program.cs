using System;
using System.Drawing;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.MacOS;
using Hawkynt.NativeForms.Backends.Windows;

namespace Crush.Viewer;

internal static class Program {

  [STAThread]
  public static int Main(string[] args) {
    BackendRegistry.Register(new Win32Backend());
    BackendRegistry.Register(new GtkBackend());
    BackendRegistry.Register(new CocoaBackend());

    var options = ViewerLaunchOptions.Parse(args);
    if (options.ScreenshotPath == null && Array.IndexOf(args, "--screenshot") >= 0) {
      Console.Error.WriteLine("--screenshot needs a usable file path to write the picture to.");
      return 2;
    }

    // The toolkit ships a macOS package for completeness whose every entry point throws, so on macOS
    // there is nothing to put on screen. Saying so and leaving is the honest answer; resolving a
    // backend that reports itself unsupported and building a window on it only moves the failure.
    IPlatformBackend backend;
    try {
      backend = BackendRegistry.Resolve();
      if (!backend.IsSupported)
        return _NoBackend($"the {backend.Name} backend reports this platform as unsupported", options);
    } catch (Exception ex) {
      return _NoBackend(ex.Message, options);
    }

    try {
      return options.ScreenshotPath != null
        ? _RunScreenshot(backend, options)
        : _RunInteractive(backend, options);
    } catch (PlatformNotSupportedException ex) {
      // A backend may call itself supported and still refuse to make anything: the macOS package
      // answers every call this way. The refusal arrives at the first widget, not at Resolve.
      return _NoBackend(ex.Message, options);
    }
  }

  private static int _NoBackend(string reason, ViewerLaunchOptions options) {
    Console.Error.WriteLine($"No user-interface backend supports this platform: {reason}.");
    if (!options.SmokeTest)
      return 3;

    // A start-up check cannot demand a window where no backend can make one. It has still proved
    // that the application starts, reads its command line and exits cleanly, which is what it is for.
    Console.Error.WriteLine("Start-up check passed without a window; nothing was drawn.");
    return 0;
  }

  private static int _RunInteractive(IPlatformBackend backend, ViewerLaunchOptions options) {
    var shell = new ViewerShell(backend);
    _SelectTab(shell, options);
    if (options.InitialPath != null)
      shell.Load += (_, _) => shell.OpenPath(options.InitialPath);

    if (options.SmokeTest)
      _CloseAfter(shell, 400);

    Application.Run(shell, backend);
    return 0;
  }

  private static int _RunScreenshot(IPlatformBackend backend, ViewerLaunchOptions options) {
    if (!WindowCapture.IsSupported) {
      Console.Error.WriteLine(WindowCapture.UnsupportedReason);
      return 4;
    }

    DirectoryInfo? fixture = null;
    var status = 5;
    try {
      var path = options.InitialPath;
      if (path == null) {
        fixture = _CreateScreenshotFixture();
        path = fixture.FullName;
      }

      var shell = new ViewerShell(backend);
      if (!_SelectTab(shell, options))
        return 2;

      if (options.Command != null && !ViewerShell.KnowsCommand(options.Command)) {
        Console.Error.WriteLine(
          $"--command '{options.Command}' names no pixel command; they are Resample, {string.Join(", ", Array.ConvertAll(PixelCommands.All, c => c.Text.TrimEnd('…')))}.");
        return 2;
      }

      _FitToScreen(shell);
      var opened = path;
      shell.Load += (_, _) => shell.OpenPath(opened);

      // Posted rather than run from Load: a command with settings opens a modal dialog, and a modal
      // loop started before the window's own loop exists never gets to paint.
      if (options.Command is { } command)
        shell.Load += (_, _) => shell.BeginInvoke(() => shell.RunCommand(command, options.CommandWithoutAsking));
      else if (options.RedEyeArea is { } eye)
        shell.Load += (_, _) => shell.BeginInvoke(() => shell.ReduceRedEye(eye));

      // The window has to be mapped and painted before the screen holds anything worth reading, and
      // the thumbnail decoder needs a moment to fill the browser, so the capture waits rather than
      // firing from Load. How long that takes is not something to guess at once: a capture that
      // comes back empty is tried again before it is called a failure. A command still running in
      // the background is waited for the same way, so the capture shows its result.
      var attempts = 0;
      var timer = new Timer { Interval = _CAPTURE_DELAY_MS };
      timer.Tick += (_, _) => {
        ++attempts;
        var last = attempts >= _CAPTURE_ATTEMPTS;
        if (shell.IsBusy && !last)
          return;

        var dialog = shell.OpenDialog;
        var captured = dialog != null
          ? _Capture(dialog, options.ScreenshotPath!, reportFailure: last)
          : _Capture(shell, options.ScreenshotPath!, reportFailure: last);
        if (!captured && !last)
          return;

        timer.Stop();
        status = captured ? 0 : 5;
        if (dialog != null) {
          // The window is closed from its own loop once the dialog's has ended; closing it from
          // inside the dialog's loop would leave that loop running over a closed owner.
          dialog.DialogResult = DialogResult.Cancel;
          shell.BeginInvoke(shell.Close);
          return;
        }

        shell.Close();
      };
      timer.Start();

      Application.Run(shell, backend);
      return status;
    } catch (Exception ex) {
      Console.Error.WriteLine($"The viewer could not be captured: {ex.Message}");
      return 5;
    } finally {
      if (fixture != null)
        try {
          fixture.Delete(true);
        } catch (IOException) {
          // A leftover folder under the temporary directory is not worth failing the run over.
        }
    }
  }

  /// <summary>Parks the window at the corner of the screen and keeps it inside it.</summary>
  /// <remarks>
  /// The capture reads the screen, so anything hanging off it is not there to be read. Capture
  /// machines are small — a 1024x768 session is normal — and the viewer's default window is larger
  /// than that, so it is sized down for the shot rather than photographed in pieces.
  /// </remarks>
  private static void _FitToScreen(Form shell) {
    var screen = WindowCapture.ScreenSize;
    if (screen.Width < 1 || screen.Height < 1)
      return;

    shell.StartPosition = FormStartPosition.Manual;
    shell.ClientSize = new(
      Math.Min(shell.ClientSize.Width, screen.Width),
      Math.Min(shell.ClientSize.Height, Math.Max(240, screen.Height - _TITLE_BAR_ALLOWANCE)));
    shell.Location = new(0, 0);
  }

  private const int _TITLE_BAR_ALLOWANCE = 48;
  private const int _CAPTURE_DELAY_MS = 1500;
  private const int _CAPTURE_ATTEMPTS = 6;

  private static bool _Capture(Form shell, string path, bool reportFailure) {
    var origin = shell.PointToScreen(new Point(0, 0));
    var size = shell.ClientSize;
    var captured = WindowCapture.Capture(origin.X, origin.Y, size.Width, size.Height);
    if (captured == null) {
      if (reportFailure)
        Console.Error.WriteLine(
          $"No pixels came back for {size.Width} x {size.Height} at {origin.X},{origin.Y}: either the platform "
          + "refused the read, or the window in front there belongs to another application.");
      return false;
    }

    // A capture that reads one flat colour caught the desktop behind the window, or a window that
    // never painted. Writing it anyway would leave a screenshot job that looks green and produces a
    // black rectangle, which is worse than a job that says it failed.
    if (WindowCapture.IsBlank(captured)) {
      if (reportFailure)
        Console.Error.WriteLine($"The capture at {origin.X},{origin.Y} ({size.Width} x {size.Height}) is one flat colour; the viewer window was not on screen there.");
      return false;
    }

    var target = new FileInfo(Path.GetFullPath(path));
    target.Directory?.Create();
    if (FormatRegistry.Write(captured, ImageFormat.Png, target))
      return true;

    Console.Error.WriteLine("The PNG writer refused the captured window.");
    return false;
  }

  /// <summary>Opens the ribbon on the tab the command line named, and says so when it names none.</summary>
  /// <remarks>
  /// A capture of the wrong tab looks exactly like a successful capture, so a name that matches no
  /// tab is an error rather than a quiet fall back to the first.
  /// </remarks>
  private static bool _SelectTab(ViewerShell shell, ViewerLaunchOptions options) {
    if (options.Tab == null || shell.SelectTab(options.Tab))
      return true;

    Console.Error.WriteLine($"--tab '{options.Tab}' names no ribbon tab; the tabs are {string.Join(", ", shell.TabNames)}.");
    return false;
  }

  private static void _CloseAfter(Form form, int milliseconds) {
    // Closing from Load runs before the platform's event loop exists and wedges it; the close has to
    // come from inside the loop.
    var timer = new Timer { Interval = milliseconds };
    timer.Tick += (_, _) => {
      timer.Stop();
      form.Close();
    };
    timer.Start();
  }

  /// <summary>Writes a handful of deterministic pictures so a capture shows a populated window.</summary>
  private static DirectoryInfo _CreateScreenshotFixture() {
    var folder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"crush-viewer-{Guid.NewGuid():N}"));
    folder.Create();

    (string Name, int Width, int Height, int Tile)[] plan = [
      ("01-gradient.png", 960, 540, 80),
      ("02-checker.png", 480, 480, 32),
      ("03-bands.png", 640, 400, 16),
      ("04-corner.png", 512, 288, 64),
      ("05-fine.png", 400, 400, 8),
      ("06-wide.png", 800, 300, 40),
    ];

    foreach (var (name, width, height, tile) in plan) {
      var image = _Fixture(width, height, tile);
      if (!FormatRegistry.Write(image, ImageFormat.Png, new FileInfo(Path.Combine(folder.FullName, name))))
        throw new InvalidOperationException("The PNG writer could not create the screenshot fixture.");
    }

    return folder;
  }

  private static RawImage _Fixture(int width, int height, int tile) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var i = (y * width + x) * 4;
        var checker = (x / tile + y / tile) & 1;
        var fx = x / (double)(width - 1);
        var fy = y / (double)(height - 1);
        pixels[i] = (byte)(35 + 150 * fy + checker * 18);
        pixels[i + 1] = (byte)(45 + 145 * fx);
        pixels[i + 2] = (byte)(160 + 70 * (1 - fy));
        pixels[i + 3] = 255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }
}
