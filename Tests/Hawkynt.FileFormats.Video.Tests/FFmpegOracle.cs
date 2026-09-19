using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// Runs FFmpeg over a file this package wrote and reports whether it got the picture back.
/// </summary>
/// <remarks>
/// The video suite has always kept its FFmpeg evidence as committed digests rather than as calls,
/// because a build machine has no FFmpeg on it. That is the right shape for a measurement over a
/// corpus — a hundred clips cannot be carried around — and the wrong one for the Oracle column,
/// which claims a tool has read what an encoder writes and has to be able to be wrong about it. So
/// the claim is executable: the file is handed over and the frame that comes back has to be the size
/// that went in. Where FFmpeg is absent the check reports inconclusive, exactly as the image
/// package's oracles do, and the rest of the suite runs untouched.
/// <para/>
/// A decode into a picture rather than <c>-f null</c>. FFmpeg will happily walk a container it
/// cannot decode a frame of and exit zero, so "it did not complain" is not evidence that anything
/// was decoded; a PNG of the right geometry is.
/// </remarks>
internal static class FFmpegOracle {

  private const int _TIMEOUT_MILLISECONDS = 60_000;

  /// <summary>Where FFmpeg is, or null when the machine has not got it.</summary>
  public static string? ExecutablePath { get; } = _Locate();

  public static bool IsAvailable => ExecutablePath != null;

  /// <summary>Marks the calling test inconclusive when FFmpeg is missing.</summary>
  public static void RequireAvailable() {
    if (!IsAvailable)
      Assert.Inconclusive("no ffmpeg on this machine to ask. Set FFMPEG to the binary or put it on PATH.");
  }

  /// <summary>
  /// The words FFmpeg prints for <c>AVERROR_PATCHWELCOME</c>.
  /// </summary>
  /// <remarks>
  /// This is the one thing FFmpeg can say that is about the binary rather than about the bytes it was
  /// handed. <c>avpriv_request_sample</c> raises it where a decoder meets a feature the build does not
  /// implement, and nothing else produces it: a stream that is malformed answers "Invalid data", a
  /// stream that decodes differently answers nothing at all and is caught by comparing what came out.
  /// So a check may branch on it without any risk of swallowing a disagreement, which is what makes it
  /// worth having a named constant instead of a version number — a version number stops being true the
  /// moment a distribution backports a patch, and this does not.
  /// </remarks>
  public const string NOT_IMPLEMENTED = "Not yet implemented in FFmpeg, patches welcome";

  /// <summary>Whether FFmpeg answered that this build has not implemented what the file needs.</summary>
  public static bool SaysItHasNotImplementedThis(string diagnostics)
    => diagnostics.Contains(NOT_IMPLEMENTED, StringComparison.Ordinal);

  /// <summary>
  /// Which feature FFmpeg says it has not implemented, in FFmpeg's own words.
  /// </summary>
  /// <remarks>
  /// <c>avpriv_request_sample</c> names the feature at warning level and the error level beneath it
  /// carries only the generic <see cref="NOT_IMPLEMENTED"/> sentence, so the decode is run once more
  /// one level louder purely to quote the name. Only ever on the way to a skip, never on the way to a
  /// verdict: what is measured stays measured at <c>-loglevel error</c>, where a build that reads the
  /// file cleanly says nothing whatever.
  /// </remarks>
  public static string WhatItSaysIsMissing(string path) {
    const string BOILERPLATE = ". Update your FFmpeg version";

    var startInfo = new ProcessStartInfo(ExecutablePath!) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    foreach (var argument in new[] {
      "-hide_banner", "-loglevel", "warning", "-y", "-i", path, "-an", "-f", "null", "-",
    })
      startInfo.ArgumentList.Add(argument);

    try {
      using var process = Process.Start(startInfo);
      if (process == null)
        return NOT_IMPLEMENTED;

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return NOT_IMPLEMENTED;
      }

      var named = string.Concat(stdout.Result, stderr.Result)
        .Split('\n')
        .Select(static line => line.Trim())
        .FirstOrDefault(static line => line.Contains(BOILERPLATE, StringComparison.Ordinal));

      return named == null
        ? NOT_IMPLEMENTED
        : _WithoutTheAllocationAddress(named[..named.IndexOf(BOILERPLATE, StringComparison.Ordinal)]);
    } catch (Exception) {
      return NOT_IMPLEMENTED;
    }
  }

  /// <summary>
  /// The build that answered, in its own words: the first line of <c>ffmpeg -version</c>.
  /// </summary>
  /// <remarks>
  /// For attributing an answer and for nothing else. No check here decides anything from a version
  /// number — a build number is a guess at a capability, and a wrong one as soon as somebody backports
  /// — so what is acted on is always what the binary did when it was asked. This is how a skip says
  /// which binary it was that could not.
  /// </remarks>
  public static string Banner { get; } = _ReadBanner();

  private static string _ReadBanner() {
    if (ExecutablePath == null)
      return "no ffmpeg";

    var startInfo = new ProcessStartInfo(ExecutablePath) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    foreach (var argument in new[] { "-hide_banner", "-version" })
      startInfo.ArgumentList.Add(argument);

    try {
      using var process = Process.Start(startInfo);
      if (process == null)
        return ExecutablePath;

      var stdout = process.StandardOutput.ReadToEndAsync();
      process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return ExecutablePath;
      }

      var first = stdout.Result.Split('\n').Select(static line => line.Trim()).FirstOrDefault(static line => line.Length != 0);

      return string.IsNullOrEmpty(first) ? ExecutablePath : first;
    } catch (Exception) {
      return ExecutablePath;
    }
  }

  /// <summary>Drops the <c>@ 0x…</c> out of a decoder tag, which changes every run and says nothing.</summary>
  private static string _WithoutTheAllocationAddress(string line) {
    var at = line.IndexOf(" @ 0x", StringComparison.Ordinal);
    if (at < 0)
      return line;

    var close = line.IndexOf(']', at);

    return close < 0 ? line : line[..at] + line[close..];
  }

  /// <summary>Decodes the first frame and says whether it came back at the size it went in at.</summary>
  public static (bool Decoded, string Output) TryDecodeFirstFrame(string path, int width, int height) {
    var png = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

    try {
      var startInfo = new ProcessStartInfo(ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", path, "-frames:v", "1", png })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      if (process == null)
        return (false, "ffmpeg would not start");

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();

      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return (false, "ffmpeg timed out");
      }

      var diagnostics = SignificantDiagnostics(string.Concat(stdout.Result, stderr.Result));
      var size = _PngSize(png);

      // A picture of the right size that came with a complaint is not the picture that went in.
      if (diagnostics.Length != 0)
        return (false, diagnostics);

      return (size == (width, height),
        size == null ? "it produced no picture" : $"it decoded {size.Value.Width}x{size.Value.Height}");
    } catch (Win32Exception) {
      return (false, "no ffmpeg on this machine");
    } catch (Exception exception) {
      return (false, $"{exception.GetType().Name}: {exception.Message}");
    } finally {
      try { File.Delete(png); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// Decodes the complete first video stream to unframed RGB and requires exactly the stated number
  /// of pictures. Unlike <see cref="TryDecodeFirstFrame"/>, this reaches reordered B pictures and the
  /// references behind them rather than proving only that the opening intra picture was readable.
  /// </summary>
  public static (bool Decoded, string Output) TryDecodeFrameCount(
    string path, int width, int height, int expectedFrames) {
    var (decoded, output, _) = TryDecodePictures(path, width, height, expectedFrames);
    return (decoded, output);
  }

  /// <summary>
  /// Decodes the complete first video stream to unframed RGB24 and hands back the pictures
  /// themselves, so a caller can compare what came out against what it encoded rather than only
  /// counting frames.
  /// </summary>
  /// <remarks>
  /// Counting says a decoder walked the file; only the samples say it read it. A codec whose frames
  /// are laid out wrongly — fields on the wrong rows, a picture cropped at the wrong end — produces
  /// exactly the right number of frames of exactly the right size while being wrong in every one of
  /// them.
  /// </remarks>
  public static (bool Decoded, string Output, byte[] Pictures) TryDecodePictures(
    string path, int width, int height, int expectedFrames) {
    var raw = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".rgb");

    try {
      var startInfo = new ProcessStartInfo(ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", path,
        // -fps_mode, not -vsync: the old spelling was removed in ffmpeg 7 and this machine runs 9, where
        // passing it aborts the whole command before a frame is read.
        "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough",
        "-f", "rawvideo", "-pix_fmt", "rgb24", raw,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      if (process == null)
        return (false, "ffmpeg would not start", []);

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();

      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return (false, "ffmpeg timed out", []);
      }

      var diagnostics = SignificantDiagnostics(string.Concat(stdout.Result, stderr.Result));
      if (diagnostics.Length != 0)
        return (false, diagnostics, []);

      if (!File.Exists(raw))
        return (false, "it produced no decoded video bytes", []);

      var pictures = File.ReadAllBytes(raw);
      var frameBytes = checked(width * height * 3);
      if (pictures.Length != checked(frameBytes * expectedFrames))
        return (false,
          pictures.Length % frameBytes == 0
            ? $"it decoded {pictures.Length / frameBytes} frames instead of {expectedFrames}"
            : $"it produced {pictures.Length} bytes, which is not a whole number of {width}x{height} RGB24 frames",
          []);

      return (true, $"it decoded all {expectedFrames} {width}x{height} frames", pictures);
    } catch (Win32Exception) {
      return (false, "no ffmpeg on this machine", []);
    } catch (Exception exception) {
      return (false, $"{exception.GetType().Name}: {exception.Message}", []);
    } finally {
      try { File.Delete(raw); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// What FFmpeg said that is about the file, with the one line that is about FFmpeg's own version
  /// dropped.
  /// </summary>
  /// <remarks>
  /// At <c>-loglevel error</c> FFmpeg says nothing at all about a stream it read cleanly, so anything
  /// on the error channel is it telling us it patched over something — which is the whole worth of
  /// this oracle, and the reason the list of lines that do not count is one line long, is keyed to the
  /// decoder that printed it as well as to its text, and grows only against measurement.
  /// <para/>
  /// <b><c>[h261 @ …] warning: first frame is no keyframe</c>.</b> H.261 has no I picture. Clause 3.2
  /// puts the intra/inter choice on every macroblock's own MTYPE and leaves the picture header with no
  /// intra/inter flag to set, so FFmpeg's H.261 decoder enters every picture as
  /// <c>AV_PICTURE_TYPE_P</c> and mpegvideo's "the first picture is not an I picture" complaint fires
  /// on the opening picture of every H.261 stream there has ever been. Measured rather than reasoned
  /// about: FFmpeg 4.2.2 prints it twice for a clip <b>FFmpeg's own H.261 encoder</b> wrote, exactly as
  /// it does for one written here, and FFmpeg 8.1 prints it for neither, because FFmpeg silenced it
  /// itself — <c>s->codec_id != AV_CODEC_ID_H261 /* H.261 has no keyframes */</c> guards the log call
  /// in <c>ff_mpv_alloc_dummy_frames</c>. The line therefore reports which FFmpeg is on the machine and
  /// nothing whatever about the bytes handed to it, and a check that fails on it fails by calendar.
  /// <para/>
  /// Nothing else is tolerated. The same words from any other decoder are kept, because in a format
  /// that does have an I picture they mean the encoder did not write one; so is every other line H.261
  /// can produce; and so is a picture that never arrived, which no complaint is needed to fail.
  /// </remarks>
  internal static string SignificantDiagnostics(string diagnostics) => string.Join('\n',
    diagnostics
      .Split('\n')
      .Select(static line => line.Trim())
      .Where(static line => line.Length != 0
                            && !_IsAboutFFmpegsOwnVersion(line)
                            && !_IsWestwoodVqaReachingTheEndOfItsFile(line)));

  private static bool _IsAboutFFmpegsOwnVersion(string line)
    => line.StartsWith("[h261 @ ", StringComparison.Ordinal)
       && line.EndsWith("warning: first frame is no keyframe", StringComparison.Ordinal);

  /// <summary>
  /// The line FFmpeg's Westwood VQA demuxer prints when it runs out of file.
  /// </summary>
  /// <remarks>
  /// <c>wsvqa_read_packet</c> opens with <c>int ret = -1</c> and reads chunks until the read comes up
  /// short; having nothing left to hand back it returns that -1, which is <c>AVERROR(EPERM)</c> and is
  /// printed as "Operation not permitted". It is the demuxer's end of stream, not a judgement on the
  /// bytes: FFmpeg prints it having already decoded every picture in the file and exits zero. What the
  /// file was is still decided by the pictures it produced and by how many — a truncated or malformed
  /// VQA fails on those, and on the chunk errors the demuxer raises before it gets here.
  /// </remarks>
  private static bool _IsWestwoodVqaReachingTheEndOfItsFile(string line)
    => line.Contains("/wsvqa @ ", StringComparison.Ordinal)
       && line.EndsWith("Error during demuxing: Operation not permitted", StringComparison.Ordinal);

  private static (int Width, int Height)? _PngSize(string path) {
    try {
      if (!File.Exists(path))
        return null;

      using var stream = File.OpenRead(path);
      Span<byte> header = stackalloc byte[24];
      if (stream.Read(header) != header.Length)
        return null;

      ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
      if (!header[..8].SequenceEqual(signature))
        return null;

      var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
      var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

      return width > 0 && height > 0 ? (width, height) : null;
    } catch (IOException) {
      return null;
    }
  }

  private static string? _Locate() {
    var configured = Environment.GetEnvironmentVariable("FFMPEG");
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
      return configured;

    var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      if (string.IsNullOrWhiteSpace(directory))
        continue;

      try {
        var candidate = Path.Combine(directory, name);
        if (File.Exists(candidate))
          return candidate;
      } catch (ArgumentException) {
        // A PATH entry with characters a path cannot hold.
      }
    }

    return null;
  }
}
