using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Conformance.Recoil.Tests;

/// <summary>
/// XnView's command-line converter, used as a third opinion on what we write.
/// </summary>
/// <remarks>
/// RECOIL knows the machines and ImageMagick knows the workstations; XnView knows a wider range of
/// both than either, and it is one of the tools this project set out to replace. Two oracles that
/// agree can still both be wrong in the same place — they share no code, but they do share the
/// habit of being lenient about the same fields — so a third that was written independently of
/// both is worth more than a fourth test against either.
/// <para/>
/// Point <c>NCONVERT</c> at the executable. Its <c>Formats.txt</c> is read from beside it: like the
/// other tools, it reports the same failure whether the bytes are wrong or the format is one it has
/// never heard of, and without the catalogue every format it lacks would count as a broken writer.
/// </remarks>
internal static class XnViewOracle {

  /// <summary>Where the converter is, or null when it was not provided.</summary>
  public static string? ExecutablePath { get; } = _Locate();

  private static string? _Locate() {
    var configured = Environment.GetEnvironmentVariable("NCONVERT");
    return configured != null && File.Exists(configured) ? configured : null;
  }

  /// <summary>The extensions its own catalogue says it reads.</summary>
  /// <remarks>
  /// Entries marked as needing Windows are left out: this is the Linux build, and counting a format
  /// it cannot load here would turn every one of them into a writer that produces unreadable files.
  /// </remarks>
  public static IReadOnlySet<string> Extensions => _extensions ??= _ReadCatalogue();

  private static HashSet<string>? _extensions;

  /// <summary>Where the extension column starts in the catalogue's fixed-width layout.</summary>
  private const int _ExtensionColumn = 49;

  /// <summary>Where the remarks column starts.</summary>
  private const int _RemarksColumn = 80;

  private static HashSet<string> _ReadCatalogue() {
    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (ExecutablePath == null)
      return found;

    var catalogue = Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "Formats.txt");
    if (!File.Exists(catalogue))
      return found;

    foreach (var line in File.ReadLines(catalogue)) {
      // Every format's line starts with its own name in brackets; anything else is prose.
      if (!line.StartsWith('[') || line.Length <= _ExtensionColumn)
        continue;

      var remarks = line.Length > _RemarksColumn ? line[_RemarksColumn..] : string.Empty;
      if (remarks.Contains("Windows only", StringComparison.OrdinalIgnoreCase))
        continue;

      var end = Math.Min(line.Length, _RemarksColumn);
      foreach (var extension in line[_ExtensionColumn..end].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        found.Add("." + extension.ToLowerInvariant());
    }

    return found;
  }

  /// <summary>Asks it to convert a file, returning whether it could.</summary>
  public static (bool Decoded, string Output) TryDecode(string path) {
    if (ExecutablePath == null)
      return (false, "no converter");

    var target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

    try {
      using var process = Process.Start(new ProcessStartInfo(ExecutablePath, ["-out", "png", "-o", target, path]) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      });

      if (process == null)
        return (false, "could not start the converter");

      var error = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
      if (!process.WaitForExit(20000)) {
        process.Kill(true);
        return (false, "the converter did not finish");
      }

      // It reports success by writing the file, not only by its exit code.
      return (process.ExitCode == 0 && File.Exists(target), error.Trim());
    } catch (Exception failure) {
      return (false, failure.Message);
    } finally {
      try { File.Delete(target); } catch { /* best effort */ }
    }
  }
}
