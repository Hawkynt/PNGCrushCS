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
/// "did it exit zero" and stronger than "did it get the geometry back". The tool is made to decode
/// the file into a PNG, and the picture that comes out is then compared, pixel by pixel and with
/// every palette resolved to colour, against what this package wrote and against what this package
/// reads back — <see cref="WriterOracleComparison"/> is where that judgement lives and why it is
/// shaped the way it is.
/// <para/>
/// Geometry alone was the old bar and it was not enough. A tool handed a name it knows and bytes it
/// does not understand may still report a size, several extensions in this registry are claimed by
/// two unrelated formats, and a reader can answer with the right width and height and then hand back
/// a canvas that has nothing of the picture in it — which is the shape the Apple IIgs <c>$C1</c>
/// defect took for as long as it survived.
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

    /// <summary>The tool read the file and rebuilt a picture that agrees with what was written.</summary>
    Accepted,

    /// <summary>The tool was asked and would not produce that picture.</summary>
    Rejected,
  }

  private const int _TIMEOUT_MILLISECONDS = 20_000;

  /// <summary>Every oracle this fixture knows how to run, in the order the table prints them.</summary>
  public static readonly ConformanceOracle[] Runnable = [
    ConformanceOracle.Recoil2Png,
    ConformanceOracle.ImageMagick,
    ConformanceOracle.XnView,
    ConformanceOracle.DWebp,
    ConformanceOracle.Djxl,
    ConformanceOracle.OpjDecompress,
    ConformanceOracle.HeifDec,
    ConformanceOracle.AvifDec,
    ConformanceOracle.FFmpeg,
    ConformanceOracle.LibreOffice,
    ConformanceOracle.IrfanView,
    ConformanceOracle.Ghostscript,
  ];

  /// <summary>Where <c>i_view64.exe</c> is, in the shape the platform running it wants.</summary>
  /// <remarks>
  /// IrfanView is a Windows program with no build for anything else, so everywhere but Windows it is
  /// started through Wine and reaches the host filesystem through the <c>Z:</c> drive. Which prefix
  /// it lives in is Wine's business — <c>WINEPREFIX</c> — and not something this has to know.
  /// </remarks>
  private static string? _IrfanView {
    get {
      var configured = Environment.GetEnvironmentVariable("IRFANVIEW");

      return string.IsNullOrWhiteSpace(configured) || !File.Exists(configured) ? null : configured;
    }
  }

  /// <summary>A host path as the Windows program under Wine has to be handed it.</summary>
  private static string _AsWindowsPath(string path)
    => OperatingSystem.IsWindows() ? path : "Z:" + path.Replace('/', '\\');

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
  /// <para/>
  /// IrfanView is in that second group for a reason worth stating, because its documentation invites
  /// the opposite conclusion: the extension it is handed does not decide which reader it uses. A PNG
  /// renamed to an extension it has never heard of comes back decoded, so a list of names would only
  /// hide answers it is willing to give.
  /// <para/>
  /// Ghostscript has one because it is not general-purpose at all. It reads the PostScript language
  /// and nothing else, and the names below are every one the four formats in that family go by.
  /// </remarks>
  public static IReadOnlyCollection<string>? OnlyForExtensions(ConformanceOracle oracle) => oracle switch {
    ConformanceOracle.DWebp => new[] { ".webp" },
    ConformanceOracle.Djxl => new[] { ".jxl" },
    ConformanceOracle.OpjDecompress => new[] { ".jp2", ".j2k", ".jpc", ".jpf", ".jpx", ".j2c" },
    ConformanceOracle.HeifDec => new[] { ".heic", ".heif", ".hif", ".avci" },
    ConformanceOracle.AvifDec => new[] { ".avif", ".avifs" },
    ConformanceOracle.Ghostscript => new[] {
      ".ps", ".ps1", ".ps2", ".ps3", ".prn", ".pdx",
      ".eps", ".epsf", ".epsi", ".epi", ".ept",
      ".ai", ".pdf",
    },
    _ => null,
  };

  /// <summary>Hands one written file to one tool and asks what it makes of it.</summary>
  /// <param name="oracle">The tool to ask.</param>
  /// <param name="path">The file this package's writer produced.</param>
  /// <param name="entry">The format that wrote it, whose own reader is the second opinion.</param>
  /// <param name="source">The picture that was handed to the writer.</param>
  public static (Verdict Verdict, string Detail) Ask(ConformanceOracle oracle, string path, FormatEntry entry, RawImage source) {
    var executable = _Executable(oracle);
    if (executable == null)
      return (Verdict.Absent, "not installed here");

    var extensions = OnlyForExtensions(oracle);
    if (extensions != null && !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
      return (Verdict.NoOpinion, "reads no format that goes by this name");

    var output = _OutputPath(oracle, path);
    try {
      var (exitCode, diagnostics) = _Run(executable, _Arguments(oracle, path, output), oracle);
      if (exitCode == null)
        return (Verdict.Rejected, diagnostics.Length == 0 ? "it would not run" : _FirstLine(diagnostics));

      // The several ways of saying "I have never heard of this format", which is the tool declining
      // to judge rather than judging. Blaming the writer for them would measure the tool's build.
      if (_IsNoOpinion(diagnostics) || _NeverHeardOfTheName(oracle, diagnostics))
        return (Verdict.NoOpinion, _FirstLine(diagnostics));

      var rebuilt = _ReadPng(output);
      if (rebuilt == null)
        return (Verdict.Rejected, diagnostics.Length == 0 ? "it produced no picture" : _FirstLine(diagnostics));

      return WriterOracleComparison.Judge(entry, path, source, rebuilt);
    } finally {
      _DeleteOutput(oracle, output);
    }
  }

  /// <summary>
  /// Hands one written file to one tool and returns the picture the tool rebuilt from it, or
  /// <c>null</c> where the tool is absent, has no reader for the name, or would not decode it.
  /// </summary>
  /// <remarks>
  /// <see cref="Ask"/> reaches a verdict and reports it in words. A caller that wants to measure the
  /// samples itself — the WebP fixture compares channel for channel against a reference of its own —
  /// needs the picture rather than the verdict, and this is where it gets it.
  /// </remarks>
  public static RawImage? Rebuild(ConformanceOracle oracle, string path) {
    var executable = _Executable(oracle);
    if (executable == null)
      return null;

    var output = _OutputPath(oracle, path);
    try {
      var (exitCode, _) = _Run(executable, _Arguments(oracle, path, output), oracle);
      if (exitCode is not 0 || !File.Exists(output))
        return null;

      return FormatRegistry.GetEntry(ImageFormat.Png)?.LoadRawImageFromBytes(File.ReadAllBytes(output));
    } catch (IOException) {
      return null;
    } finally {
      _DeleteOutput(oracle, output);
    }
  }

  private static string _OutputPath(ConformanceOracle oracle, string input) {
    if (oracle != ConformanceOracle.LibreOffice)
      return Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

    // soffice chooses the output file name from the input stem; isolate each invocation so parallel
    // tests cannot collide and so an already-running desktop instance cannot steal the conversion.
    var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(directory);
    return Path.Combine(directory, Path.GetFileNameWithoutExtension(input) + ".png");
  }

  private static void _DeleteOutput(ConformanceOracle oracle, string output) {
    try { File.Delete(output); } catch { /* best effort */ }

    if (oracle == ConformanceOracle.LibreOffice)
      try { Directory.Delete(Path.GetDirectoryName(output)!, recursive: true); } catch { /* best effort */ }
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

  /// <summary>Whether a tool that chooses its reader by content has declined to look at all.</summary>
  /// <remarks>
  /// <c>nconvert</c> picks a loader by sniffing the bytes and not by the name — a PNG called
  /// <c>.xyzzy</c> converts — and what it says on failure records how far it got. A loader that
  /// claimed the file and then could not follow it answers <c>Can't read file</c>, or complains in
  /// its own words about the field it did not like; that is a reader disagreeing, and it is a
  /// rejection. <c>Don't know how to read this picture</c> is the other answer and means no loader
  /// claimed the bytes at all.
  /// <para/>
  /// That second answer covers two cases which cannot be told apart from here: a format XnView has
  /// never implemented, and a header so wrong that the loader which would have read it did not
  /// recognise the file as its own. The column's claim is specifically that the tool read the file
  /// and disagreed, so the ambiguous answer is recorded as no opinion rather than guessed at.
  /// <para/>
  /// The catalogue beside the binary was the obvious tie-breaker — <c>Formats.txt</c>, which
  /// <c>Conformance.Recoil.Tests.XnViewOracle</c> reads for its own gating — and measuring it over
  /// this registry is what ruled it out. An extension in that table is not a reader for the format
  /// that goes by it here: taking a listed extension as proof XnView implements the format turned
  /// 120 formats it has plainly never heard of into writers it rejects, purely on our spelling of a
  /// name colliding with somebody else's.
  /// <para/>
  /// What survives the rule is the case worth having: a writer whose container is right and whose
  /// payload is not gets its loader claimed and then refused, which is the shape the defects this
  /// column exists to find actually take.
  /// </remarks>
  private static bool _NeverHeardOfTheName(ConformanceOracle oracle, string diagnostics)
    => oracle == ConformanceOracle.XnView
      && diagnostics.Contains("know how to read", StringComparison.OrdinalIgnoreCase);

  private static string[] _Arguments(ConformanceOracle oracle, string input, string output) => oracle switch {
    ConformanceOracle.Recoil2Png => ["-o", output, input],
    ConformanceOracle.ImageMagick => [input + "[0]", "png:" + output],
    // -noholder keeps it from reading a '#' or a '%' in a temporary name as a placeholder to expand,
    // and -overwrite stops it stalling on a name that already exists.
    ConformanceOracle.XnView => ["-quiet", "-noholder", "-overwrite", "-out", "png", "-o", output, input],
    ConformanceOracle.DWebp => [input, "-o", output],
    ConformanceOracle.Djxl => [input, output],
    ConformanceOracle.OpjDecompress => ["-i", input, "-o", output],
    ConformanceOracle.HeifDec => [input, output],
    ConformanceOracle.AvifDec => [input, output],
    ConformanceOracle.FFmpeg => ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-frames:v", "1", output],
    ConformanceOracle.LibreOffice => _LibreOfficeArguments(input, output),

    // Everywhere but Windows the executable is Wine and the program is its first argument. /silent
    // is what makes it usable with nobody present: without it a file it cannot read raises a dialog
    // and the process waits forever for somebody to dismiss it.
    ConformanceOracle.IrfanView => OperatingSystem.IsWindows()
      ? [input, "/convert=" + output, "/silent"]
      : [_IrfanView!, _AsWindowsPath(input), "/convert=" + _AsWindowsPath(output), "/silent"],

    ConformanceOracle.Ghostscript => [
      "-dQUIET", "-dBATCH", "-dNOPAUSE", "-dSAFER",
      "-sDEVICE=png16m",
      // The bounding box the file states rather than whatever medium the interpreter defaults to,
      // so the size that comes back is the size the file claims and not the size of a sheet of A4.
      "-dEPSCrop", "-dUseCropBox",
      "-dFirstPage=1", "-dLastPage=1",
      // One point to the pixel, which is what all four writers in this family mean: each states a
      // box the size of the picture in points, and each stretches the picture over exactly that box.
      // The PostScript and Illustrator writers used to scale by three quarters instead, as though a
      // point were a pixel at ninety-six to the inch, so that no single resolution could match both
      // conventions; they now say the same thing as the EPS and PDF writers beside them.
      "-r72",
      "-sOutputFile=" + output,
      // A page painted black before the file gets to it. Ghostscript's default sheet is white, and
      // a file that draws nothing at all therefore comes back as a white rectangle of exactly the
      // right size — which is indistinguishable from a decode to anything looking at geometry, and
      // one of the writers here does precisely that. Starting from black makes an undrawn page an
      // empty one, which is what it is.
      "-c", "<</BeginPage{pop gsave 0 0 0 setrgbcolor clippath fill grestore}>> setpagedevice",
      "-f", input,
    ],
    _ => throw new NotSupportedException($"{oracle} has no runner here."),
  };

  private static string[] _LibreOfficeArguments(string input, string output) {
    var directory = Path.GetDirectoryName(output)!;
    var profile = new Uri(Path.GetFullPath(Path.Combine(directory, "profile"))).AbsoluteUri;
    return [$"-env:UserInstallation={profile}", "--headless", "--convert-to", "png", "--outdir", directory, input];
  }

  /// <summary>Where the tool is, or null when the machine has not got it.</summary>
  /// <remarks>
  /// The environment variable comes first for every one of them, so a build that keeps its oracles
  /// somewhere other than the path can point at them one by one.
  /// </remarks>
  private static string? _Executable(ConformanceOracle oracle) {
    // The one oracle that is not a program of its own here: a Windows binary whose whereabouts is
    // IRFANVIEW and which everywhere else is started by Wine, so both have to be present.
    if (oracle == ConformanceOracle.IrfanView) {
      var irfanView = _IrfanView;

      return irfanView == null ? null : OperatingSystem.IsWindows() ? irfanView : _OnPath("wine");
    }

    var (variable, name) = oracle switch {
      ConformanceOracle.Recoil2Png => ("RECOIL2PNG", "recoil2png"),
      ConformanceOracle.ImageMagick => ("IMAGEMAGICK", "magick"),
      ConformanceOracle.XnView => ("NCONVERT", "nconvert"),
      ConformanceOracle.DWebp => ("DWEBP", "dwebp"),
      ConformanceOracle.Djxl => ("DJXL", "djxl"),
      ConformanceOracle.OpjDecompress => ("OPJ_DECOMPRESS", "opj_decompress"),
      ConformanceOracle.HeifDec => ("HEIF_DEC", "heif-dec"),
      ConformanceOracle.AvifDec => ("AVIFDEC", "avifdec"),
      ConformanceOracle.FFmpeg => ("FFMPEG", "ffmpeg"),
      ConformanceOracle.LibreOffice => ("LIBREOFFICE", "soffice"),
      ConformanceOracle.Ghostscript => ("GHOSTSCRIPT", OperatingSystem.IsWindows() ? "gswin64c" : "gs"),
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

  private static (int? ExitCode, string Diagnostics) _Run(string executable, string[] arguments, ConformanceOracle oracle) {
    var startInfo = new ProcessStartInfo(executable) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    foreach (var argument in arguments)
      startInfo.ArgumentList.Add(argument);

    // ImageMagick reads its policy and delegate configuration from the environment, and a machine
    // whose delegates shell out can take far longer than the picture warrants.
    if (oracle == ConformanceOracle.ImageMagick)
      startInfo.Environment["MAGICK_DEBUG"] = "None";

    // Wine's own commentary is louder than anything IrfanView says, and what it says is the answer
    // this is after.
    if (oracle == ConformanceOracle.IrfanView)
      startInfo.Environment["WINEDEBUG"] = "-all";

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

  /// <summary>The picture the tool rebuilt, read with this package's own PNG reader.</summary>
  /// <remarks>
  /// PNG is the one format here it is sound to read with our own code in this fixture: it has more
  /// independent checks on it than anything else in the registry, and the file being read was
  /// written by the tool under examination rather than by us. What is done with the picture
  /// afterwards is <see cref="WriterOracleComparison"/>'s business.
  /// </remarks>
  private static RawImage? _ReadPng(string path) {
    try {
      return !File.Exists(path)
        ? null
        : FormatRegistry.GetEntry(ImageFormat.Png)?.LoadRawImageFromBytes(File.ReadAllBytes(path));
    } catch (IOException) {
      return null;
    } catch (Exception) {
      // A PNG the tool wrote that our reader will not follow is the tool's answer being unreadable
      // rather than the writer being wrong, and either way there is no picture to compare.
      return null;
    }
  }
}
