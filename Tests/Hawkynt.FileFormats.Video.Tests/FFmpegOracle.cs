using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

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
/// A decode into real RGB bytes rather than <c>-f null</c>. FFmpeg will happily walk a container it
/// cannot decode a frame of and exit zero, so "it did not complain" is not evidence that anything
/// was decoded. Raw video also keeps the oracle about the input codec rather than an output muxer:
/// notably, H.261 has no ordinary I-picture flag and FFmpeg correctly treats Freeze Picture Release
/// as its key-frame analogue, while the image2 muxer warns when its first output frame is not marked
/// key even though the H.261 picture decoded successfully.
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

  /// <summary>Decodes the first frame and says whether it came back at the size it went in at.</summary>
  public static (bool Decoded, string Output) TryDecodeFirstFrame(string path, int width, int height) {
    var raw = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".rgb");

    try {
      var startInfo = new ProcessStartInfo(ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", path,
        "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw,
      })
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

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      if (diagnostics.Length != 0)
        return (false, diagnostics);

      if (!File.Exists(raw))
        return (false, "it produced no decoded video bytes");

      var expectedBytes = checked((long)width * height * 3);
      var actualBytes = new FileInfo(raw).Length;
      return (actualBytes == expectedBytes,
        actualBytes == expectedBytes
          ? $"it decoded one {width}x{height} RGB24 frame"
          : $"it produced {actualBytes} bytes instead of one {width}x{height} RGB24 frame ({expectedBytes} bytes)");
    } catch (Win32Exception) {
      return (false, "no ffmpeg on this machine");
    } catch (Exception exception) {
      return (false, $"{exception.GetType().Name}: {exception.Message}");
    } finally {
      try { File.Delete(raw); } catch { /* best effort */ }
    }
  }

  /// <summary>
  /// Decodes the complete first video stream to unframed RGB and requires exactly the stated number
  /// of pictures. Unlike <see cref="TryDecodeFirstFrame"/>, this reaches reordered B pictures and the
  /// references behind them rather than proving only that the opening intra picture was readable.
  /// </summary>
  public static (bool Decoded, string Output) TryDecodeFrameCount(
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
        "-map", "0:v:0", "-an", "-sn", "-dn", "-vsync", "0",
        "-f", "rawvideo", "-pix_fmt", "rgb24", raw,
      })
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

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      if (diagnostics.Length != 0)
        return (false, diagnostics);

      if (!File.Exists(raw))
        return (false, "it produced no decoded video bytes");

      var frameBytes = checked((long)width * height * 3);
      var actualBytes = new FileInfo(raw).Length;
      var expectedBytes = checked(frameBytes * expectedFrames);
      if (actualBytes != expectedBytes)
        return (false,
          actualBytes % frameBytes == 0
            ? $"it decoded {actualBytes / frameBytes} frames instead of {expectedFrames}"
            : $"it produced {actualBytes} bytes, which is not a whole number of {width}x{height} RGB24 frames");

      return (true, $"it decoded all {expectedFrames} {width}x{height} frames");
    } catch (Win32Exception) {
      return (false, "no ffmpeg on this machine");
    } catch (Exception exception) {
      return (false, $"{exception.GetType().Name}: {exception.Message}");
    } finally {
      try { File.Delete(raw); } catch { /* best effort */ }
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
