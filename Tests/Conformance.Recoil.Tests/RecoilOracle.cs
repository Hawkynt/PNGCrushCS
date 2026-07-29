using System;
using System.Diagnostics;
using System.IO;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Wrapper around <c>recoil2png</c>, the reference decoder from RECOIL (Retro Computer Image
/// Library). Our own reader agreeing with our own writer proves only that the two share a
/// misunderstanding — several formats round-tripped perfectly here while emitting files no other
/// tool on earth could open. RECOIL is the independent opinion.
/// </summary>
/// <remarks>
/// Build it from the RECOIL source release and point <c>RECOIL2PNG</c> at the binary, or drop it on
/// PATH:
/// <code>
/// curl -sL -o recoil.tar.gz https://downloads.sourceforge.net/project/recoil/recoil/6.4.5/recoil-6.4.5.tar.gz
/// tar xzf recoil.tar.gz &amp;&amp; cd recoil-6.4.5 &amp;&amp; make recoil2png
/// export RECOIL2PNG=$PWD/recoil2png
/// </code>
/// When the binary is absent every conformance test reports inconclusive rather than failing, so a
/// machine without it still runs the rest of the suite.
/// </remarks>
internal static class RecoilOracle {

  private static readonly Lazy<string?> _Path = new(_Locate);

  /// <summary>Full path to <c>recoil2png</c>, or <c>null</c> when it cannot be found.</summary>
  public static string? ExecutablePath => _Path.Value;

  public static bool IsAvailable => _Path.Value != null;

  /// <summary>Marks the current test inconclusive when the oracle is missing.</summary>
  public static void RequireAvailable() {
    if (!IsAvailable)
      Assert.Inconclusive(
        "recoil2png not found. Set RECOIL2PNG to the built binary or put it on PATH — " +
        "see RecoilOracle's remarks for the build steps.");
  }

  /// <summary>Runs the reference decoder over <paramref name="imagePath"/>.</summary>
  /// <returns>Whether it decoded, plus whatever it wrote to stdout/stderr.</returns>
  public static (bool Decoded, string Output) TryDecode(string imagePath) {
    var pngPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
    try {
      using var process = Process.Start(new ProcessStartInfo {
        FileName = ExecutablePath!,
        ArgumentList = { "-o", pngPath, imagePath },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      })!;

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();
      if (!process.WaitForExit(30_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return (false, "recoil2png timed out");
      }

      // A zero exit code with no output file still counts as a failure to decode.
      var decoded = process.ExitCode == 0 && File.Exists(pngPath) && new FileInfo(pngPath).Length > 0;
      return (decoded, string.Concat(stdout, stderr).Trim());
    } catch (Exception ex) {
      return (false, $"{ex.GetType().Name}: {ex.Message}");
    } finally {
      try { File.Delete(pngPath); } catch { /* best effort */ }
    }
  }

  /// <summary>Runs the reference decoder and hands back the PNG it produced.</summary>
  /// <returns>The PNG bytes, or null with the reason it failed.</returns>
  /// <remarks>
  /// This is the check that matters for a format we can only read: writing one and asking RECOIL to
  /// accept it is not an option, so instead both sides decode the same bytes and the two pictures
  /// are compared. Acceptance proves the container; agreement proves the pixels.
  /// </remarks>
  public static (byte[]? Png, string Output) TryDecodeToPng(string imagePath) {
    var pngPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
    try {
      using var process = Process.Start(new ProcessStartInfo {
        FileName = ExecutablePath!,
        ArgumentList = { "-o", pngPath, imagePath },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      })!;

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();
      if (!process.WaitForExit(30_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return (null, "recoil2png timed out");
      }

      var output = string.Concat(stdout, stderr).Trim();
      if (process.ExitCode != 0 || !File.Exists(pngPath))
        return (null, output);

      return (File.ReadAllBytes(pngPath), output);
    } catch (Exception ex) {
      return (null, $"{ex.GetType().Name}: {ex.Message}");
    } finally {
      try { File.Delete(pngPath); } catch { /* best effort */ }
    }
  }

  private static string? _Locate() {
    var configured = Environment.GetEnvironmentVariable("RECOIL2PNG");
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
      return configured;

    var name = OperatingSystem.IsWindows() ? "recoil2png.exe" : "recoil2png";
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      if (string.IsNullOrWhiteSpace(dir))
        continue;

      try {
        var candidate = Path.Combine(dir, name);
        if (File.Exists(candidate))
          return candidate;
      } catch (ArgumentException) {
        // PATH entry with invalid characters — skip it.
      }
    }

    return null;
  }
}
