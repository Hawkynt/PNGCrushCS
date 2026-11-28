using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Hqx;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Canopus HQX both ways, FFmpeg reading what the encoder writes in each of the four layouts.</summary>
[TestFixture]
public sealed class HqxVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void ChqxIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<HqxVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<HqxVideoEncoder>());
    });
  }

  [TestCase(0, 96, 64)]
  [TestCase(1, 96, 64)]
  [TestCase(2, 70, 37)]
  [TestCase(3, 70, 37)]
  [Category("Unit")]
  public void APictureComesBackClose(int layout, int width, int height) {
    var format = (HqxFormat)layout;
    var (_, frames, source) = _Encode(format, width, height);
    foreach (var frame in frames) {
      double error = 0;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          double d = (frame.Planes[0][y * frame.Strides[0] + x] >> 4) - source[y * width + x];
          error += d * d;
        }
      var psnr = 10 * Math.Log10(4095.0 * 4095.0 * width * height / Math.Max(error, 1));
      Assert.That(psnr, Is.GreaterThan(40), format.ToString());
      // A picture given without alpha is written opaque.
      if (frame.Planes.Length == 4)
        Assert.That(frame.Planes[3][0] >> 4, Is.EqualTo(4095), "alpha");
    }
  }

  [Test]
  [Category("Unit")]
  public void AnRgbaPictureKeepsItsAlpha() {
    const int width = 48, height = 32;
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      rgba[i * 4] = (byte)(i * 3);
      rgba[i * 4 + 1] = 128;
      rgba[i * 4 + 2] = 64;
      rgba[i * 4 + 3] = (byte)(i % width < width / 2 ? 255 : 40);
    }
    var encoder = HqxVideoEncoder.Create(_Stream(width, height));
    encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba }, 0, out var packet);
    HqxVideoDecoder.Create(encoder.DescribeStream()).TryDecode(packet, out var decoded);
    var back = decoded.ToRgba32();
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba64));
      Assert.That(back[3], Is.EqualTo(255).Within(2));
      Assert.That(back[(width - 1) * 4 + 3], Is.EqualTo(40).Within(2));
    });
  }

  [TestCase(0, 96, 64)]
  [TestCase(1, 96, 64)]
  [TestCase(2, 70, 37)]
  [TestCase(3, 70, 37)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int layout, int width, int height) {
    var format = (HqxFormat)layout;
    FFmpegOracle.RequireAvailable();
    var (film, frames, _) = _Encode(format, width, height);
    var alpha = format is HqxFormat.Yuv422Alpha or HqxFormat.Yuv444Alpha;
    var half = format is HqxFormat.Yuv422 or HqxFormat.Yuv422Alpha;
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, (alpha ? "yuva" : "yuv") + (half ? "422p16le" : "444p16le"));
      Assert.That(decoded, Is.True, detail);
      var at = 0;
      foreach (var frame in frames)
        foreach (var plane in alpha ? new[] { 0, 1, 2, 3 } : [0, 1, 2]) {
          var w = plane is 1 or 2 && half ? (width + 1) / 2 : width;
          for (var y = 0; y < height; ++y)
            for (var x = 0; x < w; ++x, at += 2)
              Assert.That(samples[at] | samples[at + 1] << 8, Is.EqualTo(frame.Planes[plane][y * frame.Strides[plane] + x]), $"plane {plane} at {x},{y}");
        }
      Assert.That(at, Is.EqualTo(samples.Length), "sample count");
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CHQX"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>Two pictures of gradients and an edge; returns the film, this decoder's frames, and the source luma at twelve bits.</summary>
  private static (byte[] Film, List<HqxFrame> Frames, int[] SourceLuma) _Encode(HqxFormat format, int width, int height) {
    var encoder = HqxVideoEncoder.CreateFor(_Stream(width, height), format);
    var half = format is HqxFormat.Yuv422 or HqxFormat.Yuv422Alpha;
    var chromaWidth = half ? (width + 1) / 2 : width;
    var packets = new List<CodedPacket>();
    var frames = new List<HqxFrame>();
    var luma = new int[width * height];
    for (var n = 0; n < 2; ++n) {
      var data = new byte[(width + 2 * chromaWidth) * height * 2];
      void Put(int index, int value) {
        data[index * 2] = (byte)value;
        data[index * 2 + 1] = (byte)(value >> 8);
      }
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var v = 4096 + (x * 40000 / width) + (y * 9000 / height) + (x > width / 2 ? 12000 : 0) + n * 500;
          Put(y * width + x, v);
          luma[y * width + x] = v >> 4;
        }
      for (var p = 0; p < 2; ++p)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < chromaWidth; ++x)
            Put(width * height + p * chromaWidth * height + y * chromaWidth + x, 32768 + (p == 0 ? x : -x) * 300 + y * 100);
      var picture = new RawImage { Width = width, Height = height, Format = half ? PixelFormat.Yuv422P16 : PixelFormat.Yuv444P16, PixelData = data };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      frames.Add(HqxFrame.Decode(packet.Data.Span));
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), frames, luma);
  }
}
