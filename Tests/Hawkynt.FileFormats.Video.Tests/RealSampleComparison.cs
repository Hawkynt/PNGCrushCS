using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// Decodes a directory of real films here and in FFmpeg and compares every picture's samples.
/// </summary>
/// <remarks>
/// Shared by the opt-in real-sample tests. FFmpeg is asked for the decoder's own sample layout — a
/// pixel format whose raw bytes are exactly a <see cref="RawImage"/>'s <c>PixelData</c> in this
/// package's layout — at each picture's own size (<c>-noautoscale</c>), so a match is a match of the
/// coded samples and not of a conversion. A picture is compared per non-empty packet, since FFmpeg's
/// demuxers drop empty ones.
/// </remarks>
internal static class RealSampleComparison {

  private const long _BYTE_BUDGET = 1_500_000_000;

  /// <summary>Runs the comparison and returns one line per film, failing the test on any difference.</summary>
  /// <param name="firstDifference">
  /// Where a picture first differs from FFmpeg's bytes for it, or -1; the default compares every byte. A
  /// codec whose coded planes do not cover the whole picture passes one that skips what neither decoder
  /// writes.
  /// </param>
  public static void Run(
    string variable, Func<MediaStreamInfo, bool> accepts, string ffmpegPixelFormat,
    Func<RawImage, ReadOnlyMemory<byte>, int>? firstDifference = null) {
    firstDifference ??= static (ours, theirs) => {
      var a = ours.PixelData.AsSpan();
      var b = theirs.Span;
      var i = 0;
      while (i < a.Length && a[i] == b[i])
        ++i;
      return i == a.Length ? -1 : i;
    };

    var directory = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {variable} to a directory holding real films.");
    FFmpegOracle.RequireAvailable();

    var failures = new List<string>();
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!).OrderBy(static p => p, StringComparer.Ordinal)) {
      var name = Path.GetFileName(film);
      byte[] bytes;
      MediaStreamInfo? stream;
      try {
        bytes = File.ReadAllBytes(film);
        stream = VideoFormatRegistry.ReadStreams(bytes).FirstOrDefault(s => s.Kind == MediaStreamKind.Video && accepts(s));
      } catch (Exception ex) {
        TestContext.Out.WriteLine($"{name}: not read here ({ex.Message})");
        continue;
      }
      if (stream == null) {
        TestContext.Out.WriteLine($"{name}: no matching stream");
        continue;
      }

      var decoder = VideoFormatRegistry.CreateDecoder(stream);
      var ours = new List<RawImage>();
      var sizes = new HashSet<string>();
      string? refusal = null;
      var budget = long.MaxValue;
      string? truncated = null;
      var preRoll = 0;
      using var packets = VideoFormatRegistry.ReadPackets(bytes, stream.Index).Where(static p => p.Data.Length != 0).GetEnumerator();
      while (true) {
        if (ours.Count >= budget)
          break;
        try {
          if (!packets.MoveNext())
            break;
        } catch (Exception ex) {
          // A damaged or truncated file: the container stops here, and what came before is compared.
          truncated = ex.Message;
          break;
        }
        var packet = packets.Current;
        try {
          if (!decoder.TryDecode(packet, out var picture))
            continue;
          // A picture an edit list places before the start is decoded for reference and not shown;
          // FFmpeg drops it from its output, so it is not compared.
          if (packet.PresentationTimestamp < 0) {
            ++preRoll;
            continue;
          }
          ours.Add(picture);
          sizes.Add($"{picture.Width}x{picture.Height}");
          if (ours.Count == 1)
            budget = Math.Max(1, _BYTE_BUDGET / picture.PixelData.Length);
        } catch (Exception ex) {
          refusal = $"refused picture {ours.Count}: {ex.Message}";
          break;
        }
      }

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, ffmpegPixelFormat, ours.Count, tolerateDiagnostics: truncated != null);
      if (!decoded) {
        failures.Add($"{name}: ffmpeg would not decode it: {detail}");
        continue;
      }

      var at = 0;
      var matched = 0;
      string? difference = null;
      foreach (var picture in ours) {
        var length = picture.PixelData.Length;
        if (at + length > samples.Length) {
          difference = $"ffmpeg produced fewer pictures than the {ours.Count} decoded here";
          break;
        }
        var first = firstDifference(picture, samples.AsMemory(at, length));
        if (first >= 0) {
          difference = $"picture {matched} differs from ffmpeg's at byte {first} of {length}";
          break;
        }
        at += length;
        ++matched;
      }

      total += matched;
      var line = $"{name}: {string.Join(", ", sizes)}, {matched} of {ours.Count} pictures identical to ffmpeg"
                 + (difference != null ? $"; {difference}" : "")
                 + (refusal != null ? $"; {refusal}" : "")
                 + (difference == null && at != samples.Length ? $"; ffmpeg has {samples.Length - at} bytes more" : "")
                 + (truncated != null ? $"; the container stops early ({truncated})" : "")
                 + (preRoll != 0 ? $"; {preRoll} pre-roll pictures not shown by either" : "");
      TestContext.Out.WriteLine(line);
      if (difference != null || refusal != null || (at != samples.Length && truncated == null))
        failures.Add(line);
    }

    TestContext.Out.WriteLine($"{total} pictures identical");
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }
}
