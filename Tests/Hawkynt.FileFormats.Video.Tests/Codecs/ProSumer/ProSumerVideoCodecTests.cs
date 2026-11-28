using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Brooktree ProSumer both ways, with FFmpeg as the oracle.</summary>
[TestFixture]
public sealed class ProSumerVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Bt20IsDecodedAndEncoded() {
    var stream = _Stream(160, 120);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<ProSumerVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<ProSumerVideoEncoder>());
    });
  }

  [TestCase(160, 120)]
  [TestCase(70, 37)]
  [Category("Unit")]
  public void APictureComesBackToSixBits(int width, int height) {
    var (_, ours, originals) = _Encode(width, height);
    for (var i = 0; i < ours.Count; ++i) {
      var worst = 0;
      var total = 0L;
      for (var p = 0; p < width * height; ++p) {
        var d = Math.Abs(ours[i][p] - originals[i][p]);
        worst = Math.Max(worst, d);
        total += d;
      }
      Assert.That((double)total / (width * height), Is.LessThan(3.0), $"picture {i} mean error");
    }
  }

  [TestCase(160, 120)]
  [TestCase(70, 37)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _) = _Encode(width, height);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv411p");
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>BT20_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/BT20</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real ProSumer films; point BT20_SAMPLES at them.")]
  public void EveryPictureOfARealProSumerFilmMatchesFFmpeg()
    => RealSampleComparison.Run("BT20_SAMPLES", ProSumerVideoDecoder.Accepts, "yuv411p");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("BT20"),
    Width = width,
    Height = height,
    BitsPerPixel = 12,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
  };

  private static (byte[] Film, List<byte[]> Ours, List<byte[]> OriginalLuma) _Encode(int width, int height) {
    var encoder = ProSumerVideoEncoder.Create(_Stream(width, height));
    var decoder = ProSumerVideoDecoder.Create(encoder.DescribeStream());
    var cw = (width + 3) / 4;
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < 3; ++n) {
      var data = new byte[width * height + 2 * cw * height];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          data[y * width + x] = (byte)Math.Clamp(20 + x * 180 / width + y * 30 / height + n * 4 + (x / 9 + y / 7) % 2 * 12, 0, 255);
      for (var p = 0; p < 2; ++p)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < cw; ++x)
            data[width * height + p * cw * height + y * cw + x] = (byte)(128 + (p == 0 ? x * 2 : -x * 2) + y / 3);
      encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv411P8, PixelData = data }, n, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add((byte[])decoded.PixelData.Clone());
      originals.Add(data);
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), ours, originals);
  }
}
