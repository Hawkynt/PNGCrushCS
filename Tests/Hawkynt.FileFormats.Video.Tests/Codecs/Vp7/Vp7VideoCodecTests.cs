using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Vp7;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>On2 VP7: registration, coding round trips, the encoder's exactness, and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class Vp7VideoCodecTests {

  [TestCase("VP70")]
  [TestCase("VP71")]
  [Category("Unit")]
  public void BothTagsAreDecodedAndEncoded(string fourCc) {
    var stream = _Stream(32, 32, fourCc);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Vp7VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters(fourCc)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnInterframeIsRefused() {
    var decoder = Vp7VideoDecoder.Create(_Stream(32, 32, "VP70"));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0x01, 0x10, 0, 0, 0, 0 }), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacketGivesNoPicture() {
    var decoder = Vp7VideoDecoder.Create(_Stream(32, 32, "VP70"));
    Assert.That(decoder.TryDecode(new(0, Array.Empty<byte>()), out _), Is.False);
  }

  [TestCase(48, 32)]
  [TestCase(160, 120)]
  [TestCase(70, 38)]
  [TestCase(1, 1)]
  [Category("Unit")]
  public void AKeyFrameComesBackClose(int width, int height) {
    var picture = _Gradient(width, height, 3);
    var encoder = Vp7VideoEncoder.Create(_Stream(width, height, "VP70"));
    var decoder = Vp7VideoDecoder.Create(encoder.DescribeStream());
    encoder.TryEncode(picture, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)));
    });
    var psnr = _LumaPsnr(picture, decoded);
    TestContext.Out.WriteLine($"{width}x{height}: luma {psnr:F2} dB, {packet.Data.Length} bytes");
    Assert.That(psnr, Is.GreaterThan(width == 1 ? 25.0 : 34.0));
  }

  [Test]
  [Category("Unit")]
  public void InterFramesFollowAMovingPicture() {
    var encoder = Vp7VideoEncoder.Create(_Stream(96, 64, "VP70"));
    var decoder = Vp7VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 10; ++i) {
      var picture = _Moving(96, 64, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(_LumaPsnr(picture, decoded), Is.GreaterThan(32.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  /// <summary>
  /// With the loop filter off, what the encoder predicts from inside a frame is exactly what the
  /// decoder rebuilds — the check that its mode, residue and DC prediction bookkeeping are the
  /// decoder's own.
  /// </summary>
  [TestCase(0)]
  [TestCase(1)]
  [Category("Unit")]
  public void TheEncodersReconstructionIsTheDecodersPicture(int profile) {
    var settings = new Vp7EncoderSettings(Profile: profile, FilterLevel: 0, GoldenInterval: 2);
    var encoder = Vp7VideoEncoder.Create(_Stream(80, 48, "VP70"), settings);
    for (var i = 0; i < 14; ++i) {
      encoder.TryEncode(i % 5 == 4 ? _Brightened(_Moving(80, 48, i), 10 * i) : _Moving(80, 48, i), i, out _);
      Assert.That(encoder.Encoder.Decoder.Last!.Luma, Is.EqualTo(encoder.Encoder.Working.Luma), $"luma of picture {i}");
      Assert.That(encoder.Encoder.Decoder.Last!.Cb, Is.EqualTo(encoder.Encoder.Working.Cb), $"chroma of picture {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ABrightnessChangeIsCodedAsAFade() {
    var encoder = Vp7VideoEncoder.Create(_Stream(64, 48, "VP70"));
    var decoder = Vp7VideoDecoder.Create(encoder.DescribeStream());
    var fades = new List<int>();
    for (var i = 0; i < 6; ++i) {
      var picture = _Brightened(_Gradient(64, 48, 1), 8 * i);
      encoder.TryEncode(picture, i, out var packet);
      decoder.TryDecode(packet, out var decoded);
      fades.Add(decoder.Fade.Alpha);
      Assert.That(_LumaPsnr(picture, decoded), Is.GreaterThan(34.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", fades));
    Assert.That(fades.Skip(1), Has.All.InRange(5, 8));
  }

  private static IEnumerable<TestCaseData> _Variants() {
    yield return new TestCaseData(new Vp7EncoderSettings(), 64, 48).SetName("profile 0, normal filter");
    yield return new TestCaseData(new Vp7EncoderSettings(SimpleFilter: true, FilterLevel: 20), 64, 48).SetName("profile 0, simple filter");
    yield return new TestCaseData(new Vp7EncoderSettings(Profile: 1), 64, 48).SetName("profile 1, normal filter");
    yield return new TestCaseData(new Vp7EncoderSettings(Profile: 1, SimpleFilter: true, FilterLevel: 30), 64, 48).SetName("profile 1, simple filter");
    yield return new TestCaseData(new Vp7EncoderSettings(Quantizer: 2, FilterLevel: 0, GoldenInterval: 1), 70, 38).SetName("fine quantiser, no filter, odd size");
    yield return new TestCaseData(new Vp7EncoderSettings(Quantizer: 120, FilterLevel: 63, GoldenInterval: 3), 112, 80).SetName("coarse quantiser, strongest filter");
  }

  [TestCaseSource(nameof(_Variants))]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(object variant, int width, int height) {
    var settings = (Vp7EncoderSettings)variant;
    FFmpegOracle.RequireAvailable();
    var encoder = Vp7VideoEncoder.Create(_Stream(width, height, "VP70"), settings);
    var decoder = Vp7VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var overwritten = new HashSet<int>();
    for (var i = 0; i < 16; ++i) {
      var picture = i % 7 == 0 ? _Gradient(width, height, i)
        : i % 5 == 3 ? _Brightened(_Moving(width, height, i), 12 * i - 60)
        : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData);
      if (decoder.FadedInPlace)
        overwritten.Add(i - 1);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Has.Length.EqualTo(ours.Sum(static b => b.Length)));

      // FFmpeg fades the previous frame's buffer in place when the golden frame is another, and
      // that buffer is the picture it has just handed out; whether its copy is the faded one is a
      // race with its own output thread. Those pictures alone are not compared.
      var size = ours[0].Length;
      for (var i = 0; i < ours.Count; ++i)
        if (!overwritten.Contains(i))
          Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
      TestContext.Out.WriteLine($"{ours.Count - overwritten.Count} of {ours.Count} pictures compared; {overwritten.Count} faded in place after FFmpeg handed them out");
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
      pixels[at] = (byte)Math.Clamp(x * 255 / Math.Max(1, width) + random.Next(-4, 5), 0, 255);
      pixels[at + 1] = (byte)Math.Clamp(y * 255 / Math.Max(1, height) + random.Next(-4, 5), 0, 255);
      pixels[at + 2] = (byte)Math.Clamp(128 + (x - y) * 2 + random.Next(-4, 5), 0, 255);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Moving(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 4 + step * 3 && x < 24 + step * 3 && y >= 6 + step && y < 26 + step;
      pixels[at] = inside ? (byte)220 : (byte)(40 + x);
      pixels[at + 1] = inside ? (byte)200 : (byte)(90 + y);
      pixels[at + 2] = inside ? (byte)30 : (byte)(160 - x / 2);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Brightened(RawImage picture, int amount) {
    var pixels = picture.PixelData.Select(v => (byte)Math.Clamp(v + amount, 0, 255)).ToArray();
    return new() { Width = picture.Width, Height = picture.Height, Format = picture.Format, PixelData = pixels };
  }

  private static double _LumaPsnr(RawImage source, RawImage decoded) {
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8).GetPlaneData(0).ToArray();
    var ours = decoded.GetPlaneData(0).ToArray();
    var mse = reference.Zip(ours, static (a, b) => (double)(a - b) * (a - b)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
