using System;
using System.Globalization;
using System.IO;
using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>What the command line asked the viewer to do.</summary>
/// <param name="InitialPath">The picture or folder to open, or <c>null</c> to open nothing.</param>
/// <param name="ScreenshotPath">Where to write a capture of the window, or <c>null</c> to run normally.</param>
/// <param name="SmokeTest">Whether to open the window, prove it paints and close it again.</param>
/// <param name="Tab">
/// Which ribbon tab to open on, by name. There for the capture: a ribbon is six tabs and a
/// screenshot shows one, so a capture run has to be able to say which.
/// </param>
/// <param name="Command">
/// A pixel command to run on the opened picture, by its button text. Also there for the capture: a
/// command with settings opens its dialog and the capture is of the dialog, preview and all.
/// </param>
/// <param name="CommandWithoutAsking">
/// Run <paramref name="Command"/> straight away on the values its dialog would open on, so the
/// capture is of the window with the result — the after half of a before-and-after pair.
/// </param>
/// <param name="RedEyeArea">
/// An area of the opened picture, in its pixels, to open the red-eye dialog on — what dragging over
/// the canvas does, for a capture run that has no mouse.
/// </param>
internal sealed record ViewerLaunchOptions(
  string? InitialPath,
  string? ScreenshotPath,
  bool SmokeTest,
  string? Tab,
  string? Command = null,
  bool CommandWithoutAsking = false,
  PixelRect? RedEyeArea = null) {

  internal static ViewerLaunchOptions Parse(string[] args) {
    string? initialPath = null;
    string? screenshotPath = null;
    string? tab = null;
    string? command = null;
    var withoutAsking = false;
    PixelRect? redEye = null;
    var smokeTest = false;

    for (var i = 0; i < args.Length; ++i)
      switch (args[i]) {
        case "--screenshot" when i + 1 < args.Length:
          screenshotPath = _FullPath(args[++i]);
          break;
        case "--smoke-test":
          smokeTest = true;
          break;
        case "--tab" when i + 1 < args.Length:
          tab = args[++i];
          break;
        case "--command" when i + 1 < args.Length:
          command = args[++i];
          withoutAsking = false;
          break;
        case "--apply" when i + 1 < args.Length:
          command = args[++i];
          withoutAsking = true;
          break;
        case "--red-eye" when i + 1 < args.Length:
          redEye = _Rectangle(args[++i]);
          break;
        case "--open" when i + 1 < args.Length:
          initialPath = _FullPath(args[++i]);
          break;
        default:
          if (!args[i].StartsWith('-') && initialPath == null)
            initialPath = _FullPath(args[i]);
          break;
      }

    return new(initialPath, screenshotPath, smokeTest, tab, command, withoutAsking, redEye);
  }

  /// <summary>Reads "left,top,width,height"; anything else is taken as not given.</summary>
  private static PixelRect? _Rectangle(string value) {
    var parts = value.Split(',');
    if (parts.Length != 4)
      return null;

    var numbers = new int[4];
    for (var i = 0; i < 4; ++i)
      if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
        return null;

    return numbers[2] > 0 && numbers[3] > 0 ? new PixelRect(numbers[0], numbers[1], numbers[2], numbers[3]) : null;
  }

  /// <summary>Resolves a path argument, treating an unusable one as though it were not given.</summary>
  /// <remarks>
  /// An empty or malformed argument throws out of <see cref="Path.GetFullPath(string)"/>, which on
  /// the command line means the application dies before it has printed anything at all.
  /// </remarks>
  private static string? _FullPath(string value) {
    if (string.IsNullOrWhiteSpace(value))
      return null;

    try {
      return Path.GetFullPath(value);
    } catch (ArgumentException) {
      return null;
    } catch (NotSupportedException) {
      return null;
    } catch (PathTooLongException) {
      return null;
    }
  }
}
