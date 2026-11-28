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

/// <summary>
/// On2 VP4: registration, coding round trips, and FFmpeg reading what is written.
/// </summary>
/// <remarks>
/// The decoder's reading of real streams — inter modes with motion vectors, golden frames, block
/// patterns, the motion-compensation loop filter — is checked by the opt-in real-film comparison:
/// <c>samples.ffmpeg.org/V-codecs/VP4/ot171_vp40.avi</c>, all 364 pictures identical to FFmpeg's.
/// </remarks>
[TestFixture]
public sealed class Vp4VideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Vp4HasADecoderAndAnEncoderOfItsOwn() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Vp4VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Vp4VideoEncoder>());
      Assert.That(Vp3VideoDecoder.Accepts(stream), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeIsRefused()
    => Assert.Throws<NotSupportedException>(() => Vp4VideoDecoder.Create(_Stream(0, 0)));

  [Test]
  [Category("Unit")]
  public void AnInterFrameBeforeAnyKeyFrameIsRefused() {
    var decoder = Vp4VideoDecoder.Create(_Stream(64, 48));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0x80, 0, 0, 0 }), out _));
  }

  [TestCase(160, 112)]
  [TestCase(100, 60)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void PicturesComeBackClose(int width, int height) {
    var encoder = Vp4VideoEncoder.Create(_Stream(width, height));
    var decoder = Vp4VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(_LumaPsnr(picture, decoded), Is.GreaterThan(30.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  [TestCase(".avi", 160, 112)]
  [TestCase(".mkv", 100, 60)]
  [TestCase(".avi", 352, 288)]
  [TestCase(".avi", 34, 18)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = Vp4VideoEncoder.Create(_Stream(width, height));
    var decoder = Vp4VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 40; ++i) {
      var picture = i % 13 == 7 ? _Noise(width, height, i) : i % 5 == 4 ? _Moving(width, height, i - 1) : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
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

  /// <summary>Opt-in: point <c>VP4_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/VP4</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real VP4 films; point VP4_SAMPLES at them.")]
  public void EveryPictureOfARealVp4FilmMatchesFFmpeg()
    => RealSampleComparison.Run("VP4_SAMPLES", Vp4VideoDecoder.Accepts, "yuv420p");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("VP40"),
    Handler = CodecTag.FromCharacters("VP40"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
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
