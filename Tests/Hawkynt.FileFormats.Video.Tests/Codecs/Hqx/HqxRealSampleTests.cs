using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Hqx;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Real Canopus HQX films decoded here and by FFmpeg, every plane compared sample for sample.</summary>
[TestFixture]
[Category("Conformance")]
public sealed class HqxRealSampleTests {

  /// <summary>Opt-in: point <c>HQX_SAMPLES</c> at the FATE suite's <c>canopus/hqx422.avi</c> and <c>hqx422a.avi</c>.</summary>
  [Test]
  [Explicit("Needs real HQX films; point HQX_SAMPLES at them.")]
  public void EveryPlaneOfARealHqxFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var directory = Environment.GetEnvironmentVariable("HQX_SAMPLES");
    if (string.IsNullOrWhiteSpace(directory))
      Assert.Inconclusive("Set HQX_SAMPLES to a directory of real HQX films.");
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!, "*.avi")) {
      var bytes = File.ReadAllBytes(film);
      var stream = VideoFormatRegistry.ReadStreams(bytes).First(static s => s.Kind == MediaStreamKind.Video);
      if (!stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("CHQX")))
        continue;
      var frames = VideoFormatRegistry.ReadPackets(bytes, stream.Index).Select(static p => HqxFrame.Decode(p.Data.Span)).ToList();
      var alpha = frames[0].Format is HqxFormat.Yuv422Alpha or HqxFormat.Yuv444Alpha;
      var pixels = (frames[0].Format is HqxFormat.Yuv444 or HqxFormat.Yuv444Alpha ? "yuv444p16le" : "yuv422p16le").Replace("yuv", alpha ? "yuva" : "yuv");
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, pixels, tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var at = 0;
      for (var f = 0; f < frames.Count; ++f) {
        var frame = frames[f];
        var order = alpha ? new[] { 0, 1, 2, 3 } : [0, 1, 2];
        foreach (var plane in order) {
          var width = plane is 1 or 2 && HqxShared.Planes(frame.Format).HalfChroma ? (frame.Width + 1) / 2 : frame.Width;
          for (var y = 0; y < frame.Height; ++y)
            for (var x = 0; x < width; ++x, at += 2) {
              var theirs = samples[at] | samples[at + 1] << 8;
              var ours = frame.Planes[plane][y * frame.Strides[plane] + x];
              if (theirs != ours)
                Assert.Fail($"{Path.GetFileName(film)} picture {f} plane {plane} at {x},{y}: ours {ours}, ffmpeg {theirs}");
            }
        }
      }
      TestContext.Out.WriteLine($"{Path.GetFileName(film)}: {frames.Count} pictures, {frames[0].Format}, identical to ffmpeg");
      total += frames.Count;
    }
    Assert.That(total, Is.GreaterThan(0));
  }
}
