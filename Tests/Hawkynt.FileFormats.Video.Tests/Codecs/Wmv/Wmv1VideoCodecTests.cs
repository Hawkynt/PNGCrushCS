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

/// <summary>Windows Media Video 7: registration, coding round trips and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class Wmv1VideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Wmv1HasADecoderAndAnEncoderOfItsOwn() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Wmv1VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Wmv1VideoEncoder>());
      Assert.That(MsMpeg4VideoDecoder.Accepts(stream), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeIsRefused()
    => Assert.Throws<NotSupportedException>(() => Wmv1VideoDecoder.Create(_Stream(0, 0)));

  [TestCase(176, 144)]
  [TestCase(200, 150)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void PicturesComeBackClose(int width, int height) {
    var encoder = Wmv1VideoEncoder.Create(_Stream(width, height));
    var decoder = Wmv1VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      var psnr = _LumaPsnr(picture, decoded);
      Assert.That(psnr, Is.GreaterThan(30.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  [TestCase(".avi", 176, 144)]
  [TestCase(".mkv", 200, 150)]
  [TestCase(".avi", 352, 288)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = Wmv1VideoEncoder.Create(_Stream(width, height));
    var decoder = Wmv1VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 26; ++i) {
      var picture = i % 9 == 0 ? _Noise(width, height, i) : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      File.WriteAllBytes(path, extension == ".avi"
        ? VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets)
        : VideoIO.Mux<MatroskaWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("WMV1"),
    Handler = CodecTag.FromCharacters("WMV1"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Moving(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 10 + step * 3 && x < 50 + step * 3 && y >= 12 + step && y < 52 + step;
      pixels[at] = inside ? (byte)220 : (byte)(40 + x / 2);
      pixels[at + 1] = inside ? (byte)200 : (byte)(90 + y / 2);
      pixels[at + 2] = inside ? (byte)30 : (byte)(160 - x / 3);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>Busy enough to need escapes and long runs.</summary>
  private static RawImage _Noise(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    random.NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _LumaPsnr(RawImage source, RawImage decoded) {
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8).GetPlaneData(0).ToArray();
    var ours = decoded.GetPlaneData(0).ToArray();
    var mse = reference.Zip(ours, static (a, b) => (double)(a - b) * (a - b)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
