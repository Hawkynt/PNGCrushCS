using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Lagarith: lossless round trips in all four layouts, and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class LagarithVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void LagsIsDecodedAndEncoded() {
    var stream = _Stream(32, 16, 24);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<LagarithVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<LagarithVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AnObsoleteFrameTypeIsRefusedByName() {
    var decoder = LagarithVideoDecoder.Create(_Stream(16, 16, 24));
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), out _));
  }

  [TestCase(24, 64, 48)]
  [TestCase(24, 33, 17)]
  [TestCase(32, 40, 30)]
  [TestCase(16, 50, 20)]
  [TestCase(16, 7, 5)]
  [TestCase(12, 64, 48)]
  [TestCase(12, 33, 19)]
  [Category("Unit")]
  public void EveryPictureComesBackExactly(int bitsPerPixel, int width, int height) {
    var encoder = LagarithVideoEncoder.Create(_Stream(width, height, bitsPerPixel));
    var decoder = LagarithVideoDecoder.Create(encoder.DescribeStream());
    foreach (var (picture, i) in _Pictures(width, height, bitsPerPixel).Select((p, i) => (p, i))) {
      encoder.TryEncode(picture, i, out var packet);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), $"picture {i}, {packet.Data.Length} bytes");
      TestContext.Out.WriteLine($"{bitsPerPixel} bpp {width}x{height} picture {i}: {packet.Data.Length} bytes of {picture.PixelData.Length}");
    }
  }

  [TestCase(24, ".avi")]
  [TestCase(32, ".avi")]
  [TestCase(16, ".mkv")]
  [TestCase(12, ".avi")]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePictures(int bitsPerPixel, string extension) {
    FFmpegOracle.RequireAvailable();
    const int width = 70, height = 38;
    var encoder = LagarithVideoEncoder.Create(_Stream(width, height, bitsPerPixel));
    var packets = new List<CodedPacket>();
    var pictures = _Pictures(width, height, bitsPerPixel).ToList();
    for (var i = 0; i < pictures.Count; ++i) {
      encoder.TryEncode(pictures[i], i, out var packet);
      packets.Add(packet);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      File.WriteAllBytes(path, extension == ".avi"
        ? VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets)
        : VideoIO.Mux<MatroskaWriter>([encoder.DescribeStream()], packets));
      var format = bitsPerPixel switch { 32 => "rgba", 16 => "yuv422p", 12 => "yuv420p", _ => "rgb24" };
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, format);
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(pictures.SelectMany(static p => p.PixelData).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("LAGS"),
    Handler = CodecTag.FromCharacters("LAGS"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>A gradient, noise, a moving block and a solid picture, in the layout the stream is coded in.</summary>
  private static IEnumerable<RawImage> _Pictures(int width, int height, int bitsPerPixel) {
    var random = new Random(bitsPerPixel);
    for (var kind = 0; kind < 4; ++kind) {
      var rgba = new byte[width * height * 4];
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        switch (kind) {
          case 0:
            rgba[at] = (byte)(x * 255 / width); rgba[at + 1] = (byte)(y * 255 / height); rgba[at + 2] = (byte)(x ^ y); rgba[at + 3] = (byte)(x + y);
            break;
          case 1:
            rgba[at] = (byte)random.Next(256); rgba[at + 1] = (byte)random.Next(256); rgba[at + 2] = (byte)random.Next(256); rgba[at + 3] = (byte)random.Next(256);
            break;
          case 2: {
            var inside = x > width / 4 && x < width / 2 && y > 2 && y < height - 2;
            rgba[at] = inside ? (byte)250 : (byte)10; rgba[at + 1] = inside ? (byte)5 : (byte)200; rgba[at + 2] = 128; rgba[at + 3] = inside ? (byte)0 : (byte)255;
            break;
          }
          default:
            rgba[at] = 12; rgba[at + 1] = 34; rgba[at + 2] = 56; rgba[at + 3] = 200;
            break;
        }
      }

      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba };
      yield return bitsPerPixel switch {
        32 => picture,
        24 => new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = picture.ToRgb24() },
        16 => FastRawImageConverter.Convert(picture, PixelFormat.Yuv422P8),
        _ => FastRawImageConverter.Convert(picture, PixelFormat.Yuv420P8),
      };
    }
  }
}
