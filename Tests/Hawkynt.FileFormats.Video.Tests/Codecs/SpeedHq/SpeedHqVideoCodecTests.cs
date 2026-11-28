using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>NewTek SpeedHQ in all eight forms: coding, alpha, the border column, and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class SpeedHqVideoCodecTests {

  private static readonly string[] _Forms = ["SHQ0", "SHQ1", "SHQ2", "SHQ3", "SHQ4", "SHQ5", "SHQ7", "SHQ9"];

  [Test]
  [Category("Unit")]
  public void TheTablesHaveFFmpegsSizes() {
    Assert.Multiple(() => {
      Assert.That(FileFormat.Codecs.SpeedHq.SpeedHqTables.AcCodes, Has.Length.EqualTo(123));
      Assert.That(FileFormat.Codecs.SpeedHq.SpeedHqTables.AcLevel, Has.Length.EqualTo(121));
      Assert.That(FileFormat.Codecs.SpeedHq.SpeedHqTables.AcRun, Has.Length.EqualTo(121));
    });
  }

  [TestCaseSource(nameof(_Forms))]
  [Category("Unit")]
  public void EveryFormIsDecodedAndEncoded(string fourCc) {
    var stream = _Stream(32, 16, fourCc);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<SpeedHqVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters(fourCc)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AWidthThatIsNotAMultipleOfEightIsRefused()
    => Assert.Throws<NotSupportedException>(() => SpeedHqVideoDecoder.Create(_Stream(30, 16, "SHQ2")));

  [TestCase("SHQ0", 64, 48)]
  [TestCase("SHQ2", 64, 48)]
  [TestCase("SHQ4", 64, 48)]
  [TestCase("SHQ2", 72, 40)]
  [TestCase("SHQ0", 40, 24)]
  [Category("Unit")]
  public void APictureComesBackClose(string fourCc, int width, int height) {
    var picture = _Gradient(width, height, 5, alpha: false);
    var (encoded, decoded) = _RoundTrip(picture, fourCc);
    var psnr = _Psnr(picture.ToRgb24(), decoded.ToRgb24());
    TestContext.Out.WriteLine($"{fourCc} {width}x{height}: {psnr:F2} dB, {encoded.Data.Length} bytes");
    // RGB PSNR, so 4:2:0 chroma on a small, steep gradient costs a couple of decibels.
    Assert.That(psnr, Is.GreaterThan(34.0));
  }

  [TestCase("SHQ1")]
  [TestCase("SHQ3")]
  [TestCase("SHQ5")]
  [TestCase("SHQ7")]
  [TestCase("SHQ9")]
  [Category("Unit")]
  public void AlphaComesBack(string fourCc) {
    var picture = _Gradient(48, 32, 6, alpha: true);
    var (_, decoded) = _RoundTrip(picture, fourCc);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    var source = picture.ToRgba32();
    var worst = Enumerable.Range(0, 48 * 32).Max(i => Math.Abs(source[i * 4 + 3] - decoded.PixelData[i * 4 + 3]));
    TestContext.Out.WriteLine($"{fourCc}: worst alpha error {worst}");
    // Run-length alpha is lossless; DCT alpha is quantised like luminance.
    Assert.That(worst, Is.LessThanOrEqualTo(fourCc is "SHQ7" or "SHQ9" ? 8 : 0));
  }

  [TestCase("SHQ0", 64, 48)]
  [TestCase("SHQ1", 56, 40)]
  [TestCase("SHQ2", 72, 40)]
  [TestCase("SHQ3", 64, 32)]
  [TestCase("SHQ4", 40, 24)]
  [TestCase("SHQ5", 48, 32)]
  [TestCase("SHQ7", 56, 48)]
  [TestCase("SHQ9", 64, 48)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePlanesAsThisDecoder(string fourCc, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = SpeedHqVideoEncoder.Create(_Stream(width, height, fourCc));
    var decoder = SpeedHqVideoDecoder.Create(encoder.DescribeStream());
    var alpha = fourCc is "SHQ1" or "SHQ3" or "SHQ5" or "SHQ7" or "SHQ9";
    var packets = new List<CodedPacket>();
    var ours = new List<byte>();
    for (var i = 0; i < 3; ++i) {
      encoder.TryEncode(_Gradient(width, height, i, alpha), i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.AddRange(alpha ? decoder.LastYuv!.PixelData : decoded.PixelData);
      if (alpha)
        ours.AddRange(decoder.LastAlpha!);
    }

    var (planar, ffmpegFormat) = fourCc switch {
      "SHQ0" => (true, "yuv420p"),
      "SHQ1" => (true, "yuva420p"),
      "SHQ2" => (true, "yuv422p"),
      "SHQ3" or "SHQ7" => (true, "yuva422p"),
      "SHQ4" => (true, "yuv444p"),
      _ => (true, "yuva444p"),
    };
    _ = planar;

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, ffmpegFormat);
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static (CodedPacket Packet, RawImage Decoded) _RoundTrip(RawImage picture, string fourCc) {
    var encoder = SpeedHqVideoEncoder.Create(_Stream(picture.Width, picture.Height, fourCc));
    var decoder = SpeedHqVideoDecoder.Create(encoder.DescribeStream());
    encoder.TryEncode(picture, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);
    return (packet, decoded);
  }

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

  private static RawImage _Gradient(int width, int height, int seed, bool alpha) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      pixels[at] = (byte)Math.Clamp(x * 255 / width + random.Next(-3, 4), 0, 255);
      pixels[at + 1] = (byte)Math.Clamp(y * 255 / height + random.Next(-3, 4), 0, 255);
      pixels[at + 2] = (byte)Math.Clamp(128 + (x - y) * 2 + random.Next(-3, 4), 0, 255);
      pixels[at + 3] = alpha ? (byte)(x < width / 2 ? 40 + y : 255) : (byte)255;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static double _Psnr(byte[] a, byte[] b) {
    var mse = a.Zip(b, static (x, y) => (double)(x - y) * (x - y)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
