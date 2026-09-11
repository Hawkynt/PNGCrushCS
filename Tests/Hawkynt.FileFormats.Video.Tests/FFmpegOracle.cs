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

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      var size = _PngSize(png);

      // At -loglevel error FFmpeg says nothing at all about a stream it read cleanly, so anything on
      // the error channel is it telling us it patched over something. A picture of the right size
      // that came with a complaint is not the picture that went in.
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
