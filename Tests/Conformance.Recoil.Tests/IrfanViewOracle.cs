using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Conformance.Recoil.Tests;

/// <summary>
/// IrfanView, run through Wine, as a fourth opinion on what we write.
/// </summary>
/// <remarks>
/// It is a Windows program with no Linux build, so it needs Wine and a prefix it has been installed
/// into. Set <c>IRFANVIEW</c> to the Windows path of <c>i_view64.exe</c> and <c>WINEPREFIX</c> to the
/// prefix holding it; without both, this declines to judge and the sweep falls back to the others.
/// <para/>
/// Its <c>/silent</c> switch is what makes it usable without a person present: without it a file it
/// cannot read raises a dialog and the process waits forever for somebody to dismiss it.
/// </remarks>
internal static class IrfanViewOracle {

  /// <summary>The Windows path of the executable, or null when it was not provided.</summary>
  public static string? ExecutablePath { get; } = Environment.GetEnvironmentVariable("IRFANVIEW");

  /// <summary>The Wine prefix it lives in.</summary>
  private static string? _Prefix => Environment.GetEnvironmentVariable("WINEPREFIX");

  /// <summary>Whether it can be asked at all.</summary>
  public static bool Available => ExecutablePath != null && _Prefix != null && Directory.Exists(_Prefix);

  /// <summary>
  /// The extensions it reads, from the list its own documentation carries.
  /// </summary>
  /// <remarks>
  /// Unlike the other tools this one ships no machine-readable catalogue, so the set is stated here.
  /// It is deliberately the formats IrfanView reads without a plugin plus those the standard plugin
  /// pack adds — naming one it cannot open would turn every such format into a writer that produces
  /// unreadable files, which is the measurement error this whole fixture exists to avoid.
  /// </remarks>
  public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
    ".bmp", ".dib", ".gif", ".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".tga", ".pcx",
    ".ico", ".cur", ".ani", ".wbmp", ".ppm", ".pgm", ".pbm", ".pnm", ".ras", ".sun", ".sgi",
    ".rgb", ".bw", ".xbm", ".xpm", ".wmf", ".emf", ".psd", ".pdd", ".dds", ".exr", ".hdr",
    ".jp2", ".j2k", ".jpc", ".jpf", ".jpx", ".pict", ".pct", ".pic", ".iff", ".lbm", ".ilbm",
    ".cut", ".g3", ".fax", ".dcx", ".mag", ".pcd", ".qtif", ".sff", ".sfw", ".wad", ".webp",
    ".xcf", ".flif", ".heic", ".avif", ".jxl", ".b3d", ".crw", ".cr2", ".nef", ".dng", ".arw",
    ".orf", ".raf", ".rw2", ".pef", ".srf", ".dcr", ".kdc", ".x3f", ".mrw", ".erf", ".mef",
  };

  /// <summary>Asks it to convert a file, returning whether it could.</summary>
  public static (bool Decoded, string Output) TryDecode(string path) {
    if (!Available)
      return (false, "not installed");

    // Wine reaches the host filesystem through the Z: drive, so no copying is needed.
    var source = "Z:" + path.Replace('/', '\\');
    var target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bmp");
    var windowsTarget = "Z:" + target.Replace('/', '\\');

    try {
      using var process = Process.Start(new ProcessStartInfo("wine",
          [ExecutablePath!, source, $"/convert={windowsTarget}", "/silent"]) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        Environment = { ["WINEDEBUG"] = "-all" },
      });

      if (process == null)
        return (false, "could not start it");

      var error = process.StandardError.ReadToEnd();
      if (!process.WaitForExit(30000)) {
        process.Kill(true);
        return (false, "it did not finish");
      }

      // The file it wrote is the answer; the exit code alone has been known to lie.
      return (File.Exists(target), error.Trim());
    } catch (Exception failure) {
      return (false, failure.Message);
    } finally {
      try { File.Delete(target); } catch { /* best effort */ }
    }
  }
}
