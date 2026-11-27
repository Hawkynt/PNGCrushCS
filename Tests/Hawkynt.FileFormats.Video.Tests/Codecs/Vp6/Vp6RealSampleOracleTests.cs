using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real VP6 films here and in FFmpeg and requires every coded sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>VP6_SAMPLES</c> at a directory of VP6 films — <c>samples.ffmpeg.org/V-codecs/VP6/</c>
/// (VP60/VP61/VP62 in AVI, one Huffman-coded VP6F in FLV) and <c>fate-suite.ffmpeg.org/vp6/</c> and
/// <c>flash-vp6/</c> (interlaced VP60, VP6F, VP6A with alpha). FFmpeg is asked for <c>yuv420p</c>, or
/// <c>yuva420p</c> for VP6A, at each picture's own size, so the comparison is of the coded samples.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class Vp6RealSampleOracleTests {

  private const string _SAMPLES_VARIABLE = "VP6_SAMPLES";

  /// <summary>How much decoded video one film may produce: the four-minute HD film is cut short to fit.</summary>
  private const long _BYTE_BUDGET = 1_500_000_000;

  [Test]
  [Explicit("Needs a directory of real VP6 films; point VP6_SAMPLES at one.")]
  public void EveryPictureOfARealVp6FilmMatchesFFmpeg() {
    var directory = Environment.GetEnvironmentVariable(_SAMPLES_VARIABLE);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      Assert.Inconclusive($"Set {_SAMPLES_VARIABLE} to a directory holding real VP6 films.");

    FFmpegOracle.RequireAvailable();

    var failures = new System.Collections.Generic.List<string>();
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!).OrderBy(static p => p, StringComparer.Ordinal)) {
      var name = Path.GetFileName(film);
      byte[] bytes = File.ReadAllBytes(film);
      MediaStreamInfo? stream;
      try {
        stream = VideoFormatRegistry.ReadStreams(bytes).FirstOrDefault(static s => s.Kind == MediaStreamKind.Video && Vp6VideoDecoder.Accepts(s));
      } catch (Exception ex) {
        TestContext.Out.WriteLine($"{name}: not read here ({ex.Message})");
        continue;
      }
      if (stream == null) {
        TestContext.Out.WriteLine($"{name}: no VP6 stream");
        continue;
      }

      var decoder = Vp6VideoDecoder.Create(stream);
      var alpha = stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("VP6A"));
      var ours = new System.Collections.Generic.List<(int W, int H, byte[] Y, byte[] U, byte[] V, byte[]? A)>();
      string? refusal = null;
      var budget = long.MaxValue;
      foreach (var packet in VideoFormatRegistry.ReadPackets(bytes, stream.Index).Where(static p => p.Data.Length != 0)) {
        if (ours.Count >= budget)
          break;
        try {
          decoder.TryDecode(packet, out _);
        } catch (Exception ex) {
          refusal = $"refused picture {ours.Count}: {ex.Message}";
          break;
        }
        var p = decoder.LastPlanes;
        ours.Add((decoder.Width, decoder.Height, p.Y, p.U, p.V, p.A));
        if (ours.Count == 1)
          budget = Math.Max(1, _BYTE_BUDGET / (p.Y.Length + p.U.Length + p.V.Length + (p.A?.Length ?? 0)));
      }

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, alpha ? "yuva420p" : "yuv420p", ours.Count);
      if (!decoded) {
        failures.Add($"{name}: ffmpeg would not decode it: {detail}");
        continue;
      }

      var at = 0;
      var matched = 0;
      string? difference = null;
      for (var i = 0; i < ours.Count && difference == null; ++i) {
        var (w, h, y, u, v, a) = ours[i];
        foreach (var plane in alpha ? new[] { y, u, v, a! } : [y, u, v]) {
          if (at + plane.Length > samples.Length) {
            difference = $"ffmpeg produced fewer pictures than the {ours.Count} decoded here";
            break;
          }
          if (!samples.AsSpan(at, plane.Length).SequenceEqual(plane)) {
            var first = 0;
            while (samples[at + first] == plane[first])
              ++first;
            difference = $"picture {i} ({w}x{h}) differs from ffmpeg's at plane byte {first}";
          }
          at += plane.Length;
          if (difference != null)
            break;
        }
        if (difference == null)
          ++matched;
      }

      total += matched;
      var line = $"{name}: {stream.Codec}, {matched} of {ours.Count} pictures identical to ffmpeg"
                 + (difference != null ? $"; {difference}" : "")
                 + (refusal != null ? $"; {refusal}" : "")
                 + (difference == null && refusal == null && at != samples.Length ? $"; ffmpeg has {samples.Length - at} bytes more" : "");
      TestContext.Out.WriteLine(line);
      if (difference != null || refusal != null || at != samples.Length)
        failures.Add(line);
    }

    TestContext.Out.WriteLine($"{total} pictures identical");
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }
}
