using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>SheerVideo's encoder: back through this decoder and through FFmpeg's, in each layout it writes.</summary>
[TestFixture]
public sealed class SheerVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void ShrIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<SheerVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<SheerVideoEncoder>());
    });
  }

  [TestCase(PixelFormat.Rgb24, 48, 20)]
  [TestCase(PixelFormat.Yuv444P8, 48, 20)]
  [TestCase(PixelFormat.Yuv422P8, 48, 20)]
  [TestCase(PixelFormat.Rgb24, 37, 9)]
  [TestCase(PixelFormat.Yuv422P8, 37, 9)]
  [Category("Unit")]
  public void APictureComesBackExactly(PixelFormat format, int width, int height) {
    var (_, ours, originals) = _Encode(format, width, height);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [TestCase(PixelFormat.Rgb24, 48, 20, "rgb24")]
  [TestCase(PixelFormat.Yuv444P8, 48, 20, "yuv444p")]
  [TestCase(PixelFormat.Yuv422P8, 48, 20, "yuv422p")]
  [TestCase(PixelFormat.Rgb24, 37, 9, "rgb24")]
  [TestCase(PixelFormat.Yuv422P8, 37, 9, "yuv422p")]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(PixelFormat format, int width, int height, string pixels) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _) = _Encode(format, width, height);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, pixels);
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("Shr0"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(PixelFormat format, int width, int height) {
    var encoder = SheerVideoEncoder.Create(_Stream(width, height));
    var decoder = SheerVideoDecoder.Create(encoder.DescribeStream());
    var size = format switch {
      PixelFormat.Yuv422P8 => width * height + 2 * ((width + 1) / 2) * height,
      _ => width * height * 3,
    };
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    var random = new Random(7);
    for (var n = 0; n < 3; ++n) {
      var data = new byte[size];
      for (var i = 0; i < size; ++i)
        data[i] = i % 11 == 0 ? (byte)random.Next(256) : (byte)((i / 3 + n * 5) & 0xFE);
      encoder.TryEncode(new RawImage { Width = width, Height = height, Format = format, PixelData = data }, n, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData);
      originals.Add(data);
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), ours, originals);
  }
}
