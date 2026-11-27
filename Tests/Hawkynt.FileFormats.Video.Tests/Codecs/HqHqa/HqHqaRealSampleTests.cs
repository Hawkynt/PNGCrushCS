using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.HqHqa;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Real Canopus HQ and HQA films decoded here and by FFmpeg, every plane compared sample for sample.</summary>
[TestFixture]
[Category("Conformance")]
public sealed class HqHqaRealSampleTests {

  /// <summary>Opt-in: point <c>HQ_SAMPLES</c> at the FATE suite's <c>canopus/hq.avi</c>, <c>hq25i.avi</c> and <c>hqa.avi</c>.</summary>
  [Test]
  [Explicit("Needs real HQ and HQA films; point HQ_SAMPLES at them.")]
  public void EveryPlaneOfARealHqFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var directory = Environment.GetEnvironmentVariable("HQ_SAMPLES");
    if (string.IsNullOrWhiteSpace(directory))
      Assert.Inconclusive("Set HQ_SAMPLES to a directory of real HQ and HQA films.");
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!, "*.avi")) {
      var bytes = File.ReadAllBytes(film);
      var stream = VideoFormatRegistry.ReadStreams(bytes).First(static s => s.Kind == MediaStreamKind.Video);
      if (!stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("CUVC")))
        continue;
      var frames = VideoFormatRegistry.ReadPackets(bytes, stream.Index).Select(static p => HqFrame.Decode(p.Data.Span)).ToList();
      var alpha = frames[0].Alpha;
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, alpha ? "yuva422p" : "yuv422p", tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var at = 0;
      for (var f = 0; f < frames.Count; ++f) {
        var frame = frames[f];
        foreach (var plane in alpha ? new[] { 0, 1, 2, 3 } : [0, 1, 2]) {
          var width = plane is 1 or 2 ? (frame.Width + 1) / 2 : frame.Width;
          for (var y = 0; y < frame.Height; ++y)
            for (var x = 0; x < width; ++x, ++at)
              if (samples[at] != frame.Planes[plane][y * frame.Strides[plane] + x])
                Assert.Fail($"{Path.GetFileName(film)} picture {f} plane {plane} at {x},{y}: ours {frame.Planes[plane][y * frame.Strides[plane] + x]}, ffmpeg {samples[at]}");
        }
      }
      Assert.That(at, Is.EqualTo(samples.Length), $"{Path.GetFileName(film)}: sample count");
      TestContext.Out.WriteLine($"{Path.GetFileName(film)}: {frames.Count} pictures, {(alpha ? "HQA" : "HQ")} {frames[0].Width}x{frames[0].Height}, identical to ffmpeg");
      total += frames.Count;
    }
    Assert.That(total, Is.GreaterThan(0));
  }
}
