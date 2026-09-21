using System;
using System.IO;

namespace Crush.Viewer;

/// <summary>What the command line asked the viewer to do.</summary>
internal sealed record ViewerLaunchOptions(string? InitialPath, string? ScreenshotPath, bool SmokeTest) {

  internal static ViewerLaunchOptions Parse(string[] args) {
    string? initialPath = null;
    string? screenshotPath = null;
    var smokeTest = false;

    for (var i = 0; i < args.Length; ++i)
      switch (args[i]) {
        case "--screenshot" when i + 1 < args.Length:
          screenshotPath = _FullPath(args[++i]);
          break;
        case "--smoke-test":
          smokeTest = true;
          break;
        case "--open" when i + 1 < args.Length:
          initialPath = _FullPath(args[++i]);
          break;
        default:
          if (!args[i].StartsWith('-') && initialPath == null)
            initialPath = _FullPath(args[i]);
          break;
      }

    return new(initialPath, screenshotPath, smokeTest);
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
