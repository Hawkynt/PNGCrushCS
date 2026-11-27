using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>ScreenPressor both ways, with FFmpeg as the oracle.</summary>
[TestFixture]
public sealed class ScprVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void ScprIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<ScprVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<ScprVideoEncoder>());
    });
  }

  [TestCase(96, 64)]
  [TestCase(37, 21)]
  [Category("Unit")]
  public void APictureComesBackExactly(int width, int height) {
    var (_, ours, originals) = _Encode(width, height);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [TestCase(96, 64)]
  [TestCase(37, 21)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _) = _Encode(width, height);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "bgr24");
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>SCPR_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/SCPR.avi</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real ScreenPressor films; point SCPR_SAMPLES at them.")]
  public void EveryPictureOfARealScprFilmMatchesFFmpeg()
    => RealSampleComparison.Run("SCPR_SAMPLES", ScprVideoDecoder.Accepts, "bgr24");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("SCPR"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 10),
    FrameRate = new Rational(10, 1),
  };

  /// <summary>A screen: flat panels, a gradient, noise, a window that moves, and a frame that does not change.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(int width, int height) {
    var encoder = ScprVideoEncoder.Create(_Stream(width, height));
    var decoder = ScprVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < 5; ++n) {
      var step = n == 3 ? 2 : n;
      var bgr = new byte[width * height * 3];
      var noise = new Random(11);
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = (y * width + x) * 3;
          var window = x >= step * 6 && x < step * 6 + width / 3 && y > height / 3;
          bgr[at] = (byte)(window ? 30 : y < 6 ? 200 : x * 255 / width);
          bgr[at + 1] = (byte)(window ? noise.Next(256) : 220);
          bgr[at + 2] = (byte)(window ? 90 : (x + y) & 0xF0);
        }
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = bgr };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"picture {n}");
      ours.Add(decoded.PixelData);
      originals.Add(bgr);
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), ours, originals);
  }
}
