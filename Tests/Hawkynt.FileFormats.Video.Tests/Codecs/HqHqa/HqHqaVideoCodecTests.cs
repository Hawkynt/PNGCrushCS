using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.HqHqa;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Canopus HQ and HQA both ways, FFmpeg reading what the encoder writes.</summary>
[TestFixture]
public sealed class HqHqaVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void CuvcIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<HqHqaVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<HqHqaVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AProfileSizeIsWrittenAsHqAndAnyOtherAsHqa() {
    Assert.Multiple(() => {
      Assert.That(HqEncoder.ProfileOf(720, 576), Is.EqualTo(3));
      Assert.That(HqEncoder.ProfileOf(160, 120), Is.EqualTo(11));
      Assert.That(HqEncoder.ProfileOf(70, 37), Is.EqualTo(-1));
    });
  }

  [TestCase(320, 240)]
  [TestCase(176, 144)]
  [TestCase(70, 37)]
  [Category("Unit")]
  public void APictureComesBackClose(int width, int height) {
    var (_, frames, luma) = _Encode(width, height);
    foreach (var frame in frames) {
      double error = 0;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          double d = frame.Planes[0][y * frame.Strides[0] + x] - luma[y * width + x];
          error += d * d;
        }
      Assert.That(10 * Math.Log10(255.0 * 255.0 * width * height / Math.Max(error, 1)), Is.GreaterThan(36));
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
    var encoder = HqHqaVideoEncoder.Create(_Stream(width, height));
    encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba }, 0, out var packet);
    HqHqaVideoDecoder.Create(encoder.DescribeStream()).TryDecode(packet, out var decoded);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData[3], Is.EqualTo(255).Within(3));
      Assert.That(decoded.PixelData[(width - 1) * 4 + 3], Is.EqualTo(40).Within(3));
    });
  }

  [TestCase(320, 240)]
  [TestCase(720, 576)]
  [TestCase(70, 37)]
  [TestCase(300, 70)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, frames, _) = _Encode(width, height);
    var alpha = frames[0].Alpha;
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, alpha ? "yuva422p" : "yuv422p");
      Assert.That(decoded, Is.True, detail);
      var at = 0;
      foreach (var frame in frames)
        foreach (var plane in alpha ? new[] { 0, 1, 2, 3 } : [0, 1, 2]) {
          var w = plane is 1 or 2 ? (width + 1) / 2 : width;
          for (var y = 0; y < height; ++y)
            for (var x = 0; x < w; ++x, ++at)
              Assert.That(samples[at], Is.EqualTo(frame.Planes[plane][y * frame.Strides[plane] + x]), $"plane {plane} at {x},{y}");
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
    Codec = CodecTag.FromCharacters("CUVC"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static (byte[] Film, List<HqFrame> Frames, byte[] Luma) _Encode(int width, int height) {
    var encoder = HqHqaVideoEncoder.Create(_Stream(width, height));
    var chroma = (width + 1) / 2;
    var packets = new List<CodedPacket>();
    var frames = new List<HqFrame>();
    var luma = new byte[width * height];
    for (var n = 0; n < 2; ++n) {
      var data = new byte[(width + 2 * chroma) * height];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          data[y * width + x] = luma[y * width + x] = (byte)Math.Clamp(30 + x * 150 / width + y * 40 / height + (x > width / 2 ? 30 : 0) + n * 3, 16, 235);
      for (var p = 0; p < 2; ++p)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < chroma; ++x)
            data[width * height + p * chroma * height + y * chroma + x] = (byte)(128 + (p == 0 ? x : -x) / 3 + y / 4);
      encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = data }, n, out var packet);
      packets.Add(packet);
      frames.Add(HqFrame.Decode(packet.Data.Span));
    }
    return (VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets), frames, luma);
  }
}
