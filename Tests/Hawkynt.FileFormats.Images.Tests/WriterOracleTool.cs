using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Runs one of the tools named by <see cref="ConformanceOracle"/> over a file this package wrote.
/// </summary>
/// <remarks>
/// The question every oracle is asked here is the same one, and it is deliberately stronger than
/// "did it exit zero". The tool is made to decode the file into a PNG and the picture that comes out
/// has to be the size the picture that went in was. Exit codes are cheap — a tool handed a name it
/// knows and bytes it does not understand may still report a size, and several extensions in this
/// registry are claimed by two unrelated formats, so a tool accepting the file can mean it read the
/// other one's header and stopped. A tool that reconstructs the right geometry has done more than
/// recognise the extension.
/// <para/>
/// An absent tool is not a disagreement. Every entry point here answers <see cref="Verdict.Absent"/>
/// rather than failing, so a machine without the tool runs the rest of the suite.
/// </remarks>
internal static class WriterOracleTool {

  /// <summary>What a tool made of the file.</summary>
  internal enum Verdict {

    /// <summary>The tool is not on this machine, so it has not been asked.</summary>
    Absent,

    /// <summary>The tool has no reader for this name and so has no opinion about the bytes.</summary>
    NoOpinion,

    /// <summary>The tool read the file and rebuilt the picture at the size it was written at.</summary>
    Accepted,

    /// <summary>The tool was asked and would not produce that picture.</summary>
    Rejected,
  }

  private const int _TIMEOUT_MILLISECONDS = 20_000;

  /// <summary>Every oracle this fixture knows how to run, in the order the table prints them.</summary>
  public static readonly ConformanceOracle[] Runnable = [
    ConformanceOracle.Recoil2Png,
    ConformanceOracle.ImageMagick,
    ConformanceOracle.DWebp,
    ConformanceOracle.Djxl,
    ConformanceOracle.OpjDecompress,
    ConformanceOracle.HeifDec,
    ConformanceOracle.AvifDec,
    ConformanceOracle.FFmpeg,
  ];

  /// <summary>Whether this machine has the tool at all.</summary>
  public static bool IsAvailable(ConformanceOracle oracle) => _Executable(oracle) != null;

  /// <summary>
  /// The extensions it is worth asking this tool about, or null where the tool reads many formats
  /// and the only way to find out is to ask.
  /// </summary>
  /// <remarks>
  /// A single-format decoder handed something else spends a process start to say what its own name
  /// already said. The general-purpose tools have no such list here on purpose: RECOIL's catalogue
  /// and ImageMagick's are both older than their binaries and each misses formats it reads
  /// perfectly well, so the file is handed over and the tool speaks for itself.
  /// </remarks>
  public static IReadOnlyCollection<string>? OnlyForExtensions(ConformanceOracle oracle) => oracle switch {
    ConformanceOracle.DWebp => new[] { ".webp" },
    ConformanceOracle.Djxl => new[] { ".jxl" },
    ConformanceOracle.OpjDecompress => new[] { ".jp2", ".j2k", ".jpc", ".jpf", ".jpx", ".j2c" },
    ConformanceOracle.HeifDec => new[] { ".heic", ".heif", ".hif", ".avci" },
    ConformanceOracle.AvifDec => new[] { ".avif", ".avifs" },
    _ => null,
  };

  /// <summary>Hands one written file to one tool and asks what it makes of it.</summary>
  public static (Verdict Verdict, string Detail) Ask(ConformanceOracle oracle, string path, int width, int height) {
    var executable = _Executable(oracle);
    if (executable == null)
      return (Verdict.Absent, "not installed here");

    var extensions = OnlyForExtensions(oracle);
    if (extensions != null && !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
      return (Verdict.NoOpinion, "reads no format that goes by this name");

    var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
    try {
      var (exitCode, diagnostics) = _Run(executable, _Arguments(oracle, path, output), oracle == ConformanceOracle.ImageMagick);
      if (exitCode == null)
        return (Verdict.Rejected, diagnostics.Length == 0 ? "it would not run" : _FirstLine(diagnostics));

      // Three ways of saying "I have never heard of this format", which is the tool declining to
      // judge rather than judging. Blaming the writer for them would measure the tool's build.
      if (_IsNoOpinion(diagnostics))
        return (Verdict.NoOpinion, _FirstLine(diagnostics));

      if (_RebuiltThePicture(output, width, height))
        return (Verdict.Accepted, string.Empty);

      return (Verdict.Rejected, diagnostics.Length == 0 ? "it produced no picture of that size" : _FirstLine(diagnostics));
    } finally {
      try { File.Delete(output); } catch { /* best effort */ }
    }
  }

  private static string _FirstLine(string text) {
    var trimmed = text.Trim();
    var end = trimmed.IndexOf('\n');

    return end < 0 ? trimmed : trimmed[..end].Trim();
  }

  /// <summary>
  /// Whether the tool declined to look rather than looked and disagreed.
  /// </summary>
  /// <remarks>
  /// The difference is the whole value of the oracle column, and it is not always obvious from the
  /// exit code: a tool that reads a format by shelling out to another program reports a missing
  /// helper the same way it reports a corrupt file. ImageMagick reads PDF through Ghostscript and
  /// says <c>FailedToExecuteCommand</c> when it is not installed, which is what the Windows runner
  /// does — the same file it accepts on a machine that has it. Counting that as a rejection would
  /// make the claim look false on one platform and true on another, when nothing about the bytes
  /// changed.
  /// </remarks>
  private static bool _IsNoOpinion(string diagnostics)
    => diagnostics.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("must specify image size", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("delegate failed", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("FailedToExecuteCommand", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("DelegateLibrarySupportNotBuiltIn", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("NoDecodeDelegateForThisImageFormat", StringComparison.OrdinalIgnoreCase)
      || diagnostics.Contains("UnableToOpenBlob", StringComparison.OrdinalIgnoreCase);

  private static string[] _Arguments(ConformanceOracle oracle, string input, string output) => oracle switch {
    ConformanceOracle.Recoil2Png => ["-o", output, input],
    ConformanceOracle.ImageMagick => [input + "[0]", "png:" + output],
    ConformanceOracle.DWebp => [input, "-o", output],
    ConformanceOracle.Djxl => [input, output],
    ConformanceOracle.OpjDecompress => ["-i", input, "-o", output],
    ConformanceOracle.HeifDec => [input, output],
    ConformanceOracle.AvifDec => [input, output],
    ConformanceOracle.FFmpeg => ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-frames:v", "1", output],
    _ => throw new NotSupportedException($"{oracle} has no runner here."),
  };

  /// <summary>Where the tool is, or null when the machine has not got it.</summary>
  /// <remarks>
  /// The environment variable comes first for every one of them, so a build that keeps its oracles
  /// somewhere other than the path can point at them one by one.
  /// </remarks>
  private static string? _Executable(ConformanceOracle oracle) {
    var (variable, name) = oracle switch {
      ConformanceOracle.Recoil2Png => ("RECOIL2PNG", "recoil2png"),
      ConformanceOracle.ImageMagick => ("IMAGEMAGICK", "magick"),
      ConformanceOracle.DWebp => ("DWEBP", "dwebp"),
      ConformanceOracle.Djxl => ("DJXL", "djxl"),
      ConformanceOracle.OpjDecompress => ("OPJ_DECOMPRESS", "opj_decompress"),
      ConformanceOracle.HeifDec => ("HEIF_DEC", "heif-dec"),
      ConformanceOracle.AvifDec => ("AVIFDEC", "avifdec"),
      ConformanceOracle.FFmpeg => ("FFMPEG", "ffmpeg"),
      _ => (null, null),
    };

    if (name == null)
      return null;

    var configured = Environment.GetEnvironmentVariable(variable!);
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
      return configured;

    return _OnPath(name);
  }

  private static string? _OnPath(string name) {
    var candidates = OperatingSystem.IsWindows() ? new[] { name + ".exe", name } : [name];

    foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      if (string.IsNullOrWhiteSpace(directory))
        continue;

      foreach (var candidate in candidates) {
        try {
          var full = Path.Combine(directory, candidate);
          if (File.Exists(full))
            return full;
        } catch (ArgumentException) {
          // A PATH entry with characters a path cannot hold. Not a place the tool is.
        }
      }
    }

    return null;
  }

  private static (int? ExitCode, string Diagnostics) _Run(string executable, string[] arguments, bool quiet) {
    var startInfo = new ProcessStartInfo(executable) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    foreach (var argument in arguments)
      startInfo.ArgumentList.Add(argument);

    // ImageMagick reads its policy and delegate configuration from the environment, and a machine
    // whose delegates shell out can take far longer than the picture warrants.
    if (quiet)
      startInfo.Environment["MAGICK_DEBUG"] = "None";

    try {
      using var process = Process.Start(startInfo);
      if (process == null)
        return (null, string.Empty);

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();

      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return (null, "timed out");
      }

      return (process.ExitCode, string.Concat(stdout.Result, stderr.Result));
    } catch (Win32Exception) {
      return (null, string.Empty);
    } catch (Exception exception) {
      return (null, exception.Message);
    }
  }

  /// <summary>Whether the PNG the tool wrote is the picture it was handed, in size and in content.</summary>
  /// <remarks>
  /// The geometry alone is not enough, and the case that showed it is Aseprite: ImageMagick reads
  /// the sprite's header, reports the right width and height, and hands back a canvas of zeroes. A
  /// reader that answers with the size and then decodes nothing would count as an oracle on the
  /// strength of the header, which is the opposite of what this column is for.
  /// <para/>
  /// A blank canvas and not merely a flat one. Requiring two distinct pixels sounded stronger and
  /// was wrong: several of the smallest formats here — a two-pixel Atari missile, a Degas brush —
  /// hold so little of the probe that a correct decode is one colour, and six of them were being
  /// recorded as never having been read when they had been read perfectly. All-zero is the shape the
  /// failure actually takes.
  /// <para/>
  /// Read with this package's own PNG reader, which is the one place that is sound: PNG is the
  /// format with the most independent checks on it here, and the file being read was written by the
  /// tool under examination rather than by us.
  /// </remarks>
  private static bool _RebuiltThePicture(string path, int width, int height) {
    try {
      if (!File.Exists(path))
        return false;

      var picture = FormatRegistry.GetEntry(ImageFormat.Png)?.LoadRawImageFromBytes(File.ReadAllBytes(path));
      if (picture == null || picture.Width != width || picture.Height != height)
        return false;

      return _IsNotABlankCanvas(picture);
    } catch (IOException) {
      return false;
    }
  }

  private static bool _IsNotABlankCanvas(RawImage picture) {
    var data = picture.PixelData;
    if (data == null || data.Length == 0)
      return false;

    foreach (var sample in data)
      if (sample != 0)
        return true;

    return false;
  }
}
