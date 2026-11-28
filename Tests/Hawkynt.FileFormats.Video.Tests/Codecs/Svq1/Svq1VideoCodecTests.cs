using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Sorenson Video 1: registration, YUV 4:1:0, coding, inter frames, and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class Svq1VideoCodecTests {

  [TestCase("SVQ1")]
  [TestCase("svq1")]
  [TestCase("svqi")]
  [Category("Unit")]
  public void TheDecoderTakesEveryTagAndTheEncoderWritesSvq1(string fourCc) {
    var stream = _Stream(32, 32, fourCc);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Svq1VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(_Stream(32, 32, "SVQ1")), Is.InstanceOf<Svq1VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void Yuv410HasQuarterSizeChromaRoundedUp() {
    var image = new RawImage { Width = 50, Height = 34, Format = PixelFormat.Yuv410P8, PixelData = new byte[50 * 34 + 2 * 13 * 9] };
    Assert.Multiple(() => {
      Assert.That(image.GetPlaneDimensions(1), Is.EqualTo((13, 9)));
      Assert.That(image.HasEnoughPixelData, Is.True);
      Assert.That(image.ToRgb24(), Has.Length.EqualTo(50 * 34 * 3));
    });
  }

  [TestCase(48, 32)]
  [TestCase(160, 120)]
  [TestCase(50, 34)]
  [Category("Unit")]
  public void AKeyFrameComesBackClose(int width, int height) {
    var picture = _Gradient(width, height, 3);
    var encoder = Svq1VideoEncoder.Create(_Stream(width, height, "SVQ1"));
    var decoder = Svq1VideoDecoder.Create(encoder.DescribeStream());
    encoder.TryEncode(picture, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv410P8));
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)));
    });
    var psnr = _LumaPsnr(picture, decoded);
    TestContext.Out.WriteLine($"{width}x{height}: luma {psnr:F2} dB, {packet.Data.Length} bytes");
    Assert.That(psnr, Is.GreaterThan(30.0));
  }

  [Test]
  [Category("Unit")]
  public void InterFramesFollowAMovingPicture() {
    var encoder = Svq1VideoEncoder.Create(_Stream(64, 48, "SVQ1"));
    var decoder = Svq1VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(64, 48, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(_LumaPsnr(picture, decoded), Is.GreaterThan(28.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  [TestCase(".avi")]
  [TestCase(".mov")]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension) {
    FFmpegOracle.RequireAvailable();
    const int width = 64, height = 48;
    var encoder = Svq1VideoEncoder.Create(_Stream(width, height, "SVQ1"));
    var decoder = Svq1VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 14; ++i) {
      var picture = i % 6 == 0 ? _Gradient(width, height, i) : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      File.WriteAllBytes(path, extension == ".avi"
        ? VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets)
        : VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv410p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height, string fourCc) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(fourCc),
    Handler = CodecTag.FromCharacters(fourCc),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Gradient(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)Math.Clamp(x * 255 / width + random.Next(-4, 5), 0, 255);
      pixels[at + 1] = (byte)Math.Clamp(y * 255 / height + random.Next(-4, 5), 0, 255);
      pixels[at + 2] = (byte)Math.Clamp(128 + (x - y) * 2 + random.Next(-4, 5), 0, 255);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Moving(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 4 + step * 2 && x < 20 + step * 2 && y >= 6 && y < 22;
      pixels[at] = inside ? (byte)220 : (byte)(40 + x);
      pixels[at + 1] = inside ? (byte)200 : (byte)(90 + y);
      pixels[at + 2] = inside ? (byte)30 : (byte)160;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>PSNR of the luma plane against the source converted the way the encoder converts it.</summary>
  private static double _LumaPsnr(RawImage source, RawImage decoded) {
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv410P8).GetPlaneData(0).ToArray();
    var ours = decoded.GetPlaneData(0).ToArray();
    var mse = reference.Zip(ours, static (a, b) => (double)(a - b) * (a - b)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
