using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>YLC: lossless round trips and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class YlcVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Ylc0IsDecodedAndEncoded() {
    var stream = _Stream(32, 16);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<YlcVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<YlcVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AnOddWidthIsRefused() => Assert.Throws<NotSupportedException>(() => YlcVideoDecoder.Create(_Stream(31, 16)));

  [TestCase(64, 48)]
  [TestCase(18, 7)]
  [TestCase(320, 240)]
  [Category("Unit")]
  public void EveryPictureComesBackExactly(int width, int height) {
    var encoder = YlcVideoEncoder.Create(_Stream(width, height));
    var decoder = YlcVideoDecoder.Create(encoder.DescribeStream());
    foreach (var (picture, i) in _Pictures(width, height).Select((p, i) => (p, i))) {
      encoder.TryEncode(picture, i, out var packet);
      decoder.TryDecode(packet, out var decoded);
      TestContext.Out.WriteLine($"{width}x{height} picture {i}: {packet.Data.Length} bytes of {picture.PixelData.Length}");
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), $"picture {i}");
    }
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePictures() {
    FFmpegOracle.RequireAvailable();
    const int width = 70, height = 38;
    var encoder = YlcVideoEncoder.Create(_Stream(width, height));
    var pictures = _Pictures(width, height).ToList();
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      encoder.TryEncode(pictures[i], i, out var packet);
      packets.Add(packet);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv422p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(pictures.SelectMany(static p => p.PixelData).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("YLC0"),
    Handler = CodecTag.FromCharacters("YLC0"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>4:2:2 pictures: a gradient, noise, a mostly flat one with a block, and a solid one.</summary>
  private static IEnumerable<RawImage> _Pictures(int width, int height) {
    var random = new Random(width * height);
    var luma = width * height;
    var chroma = width / 2 * height;
    for (var kind = 0; kind < 4; ++kind) {
      var data = new byte[luma + 2 * chroma];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var inside = x > width / 3 && x < width / 2 && y > 1 && y < height - 1;
          data[y * width + x] = kind switch {
            0 => (byte)(x * 3 + y * 2),
            1 => (byte)random.Next(256),
            2 => inside ? (byte)200 : (byte)16,
            _ => 77,
          };
          if ((x & 1) == 0) {
            var c = y * (width / 2) + x / 2;
            data[luma + c] = kind switch { 0 => (byte)(128 + x - y), 1 => (byte)random.Next(256), 2 => inside ? (byte)40 : (byte)128, _ => 90 };
            data[luma + chroma + c] = kind switch { 0 => (byte)(128 - x), 1 => (byte)random.Next(256), 2 => inside ? (byte)220 : (byte)128, _ => 160 };
          }
        }
      yield return new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = data };
    }
  }
}
