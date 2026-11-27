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
/// Sorenson Video 3: registration, coding round trips, and FFmpeg reading what is written.
/// </summary>
/// <remarks>
/// The decoder's reading of real streams — third- and half-sample motion, every partition size, B
/// pictures with direct and bidirectional blocks, watermarked slices — is checked by the opt-in
/// real-film comparison against <c>samples.ffmpeg.org/V-codecs/SVQ3</c>: <c>rotk_trailer_480x280.mov</c>
/// (4,108 pictures), <c>broken_sword_Large.mov</c> (2,083 within the budget), <c>Vertical400kbit.sorenson3.mov</c>
/// (1,308), <c>shrek2_tsr_qt_240.mov</c> (1,452), the watermarked
/// <c>lucy_letters_to_satan_2007.mov</c> (1,191), <c>blade_2_m480.mov</c> and <c>gl2.mov</c>, every picture identical to FFmpeg's.
/// </remarks>
[TestFixture]
public sealed class Svq3VideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Svq3HasADecoderAndAnEncoderOfItsOwn() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Svq3VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Svq3VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeAndNoSequenceHeaderIsRefused()
    => Assert.Throws<NotSupportedException>(() => Svq3VideoDecoder.Create(_Stream(0, 0)));

  [Test]
  [Category("Unit")]
  public void TheSequenceHeaderStatesThePictureSize() {
    var encoder = Svq3VideoEncoder.Create(_Stream(200, 120));
    var described = encoder.DescribeStream();
    var decoder = Svq3VideoDecoder.Create(new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("SVQ3"), CodecPrivateData = described.CodecPrivateData,
    });
    encoder.TryEncode(_Moving(200, 120, 0), 0, out var packet);
    Assert.That(decoder.TryDecode(packet, out var picture), Is.True);
    Assert.That((picture.Width, picture.Height), Is.EqualTo((200, 120)));
  }

  [Test]
  [Category("Unit")]
  public void APacketWithAnUnknownSliceHeaderIsDroppedAsFFmpegDropsIt() {
    var decoder = Svq3VideoDecoder.Create(_Stream(64, 48));
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0 }), out _), Is.False);
  }

  [TestCase(160, 112)]
  [TestCase(100, 60)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void PicturesComeBackClose(int width, int height) {
    var encoder = Svq3VideoEncoder.Create(_Stream(width, height));
    var decoder = Svq3VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"picture {i}");
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(_Psnr(picture, decoded), Is.GreaterThan(32.0), $"picture {i}");
    }

    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
    Assert.That(decoder.Flush(), Is.Empty, "a low-delay stream holds nothing back");
  }

  [Test]
  [Category("Unit")]
  public void AStillPictureCostsAboutABitAMacroblock() {
    var encoder = Svq3VideoEncoder.Create(_Stream(160, 112));
    encoder.TryEncode(_Moving(160, 112, 3), 0, out _);
    encoder.TryEncode(_Moving(160, 112, 3), 1, out _);
    encoder.TryEncode(_Moving(160, 112, 3), 2, out var still);
    Assert.That(still.Data.Length, Is.LessThan(4 + 10 + 70 / 8 + 2));
  }

  [TestCase(".avi", 160, 112)]
  [TestCase(".mkv", 100, 60)]
  [TestCase(".avi", 352, 288)]
  [TestCase(".avi", 34, 18)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = Svq3VideoEncoder.Create(_Stream(width, height));
    var decoder = Svq3VideoDecoder.Create(encoder.DescribeStream());
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
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuvj420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>SVQ3_SAMPLES</c> at films from <c>samples.ffmpeg.org/V-codecs/SVQ3</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real Sorenson Video 3 films; point SVQ3_SAMPLES at them.")]
  public void EveryPictureOfARealSvq3FilmMatchesFFmpeg()
    => RealSampleComparison.Run("SVQ3_SAMPLES", Svq3VideoDecoder.Accepts, "yuvj420p");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("SVQ3"),
    Handler = CodecTag.FromCharacters("SVQ3"),
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

  private static double _Psnr(RawImage source, RawImage decoded) {
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8).PixelData;
    var ours = decoded.PixelData;
    var mse = reference.Zip(ours, static (a, b) => (double)(a - b) * (a - b)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
