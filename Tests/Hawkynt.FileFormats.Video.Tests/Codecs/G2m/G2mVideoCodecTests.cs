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
/// Go2Meeting: registration, the Kempf encoder against the decoder and against FFmpeg.
/// </summary>
/// <remarks>
/// ePIC tiles — alone and with JPEG behind their transparent colour — Kempf tiles of every kind the
/// films use, and both cursor formats are checked by the opt-in real-film comparison over
/// <c>samples.ffmpeg.org/V-codecs/G2M2</c>, <c>G2M3</c> and <c>G2M4</c>.
/// </remarks>
[TestFixture]
public sealed class G2mVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void G2mHasADecoderAndAnEncoderOfItsOwn() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<G2mVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<G2mVideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void APacketThatIsNotGo2MeetingGivesNoPicture() {
    var decoder = G2mVideoDecoder.Create(_Stream(64, 48));
    Assert.That(decoder.TryDecode(new(0, new byte[16]), out _), Is.False);
  }

  [TestCase(64, 48)]
  [TestCase(150, 97)]
  [Category("Unit")]
  public void APictureOfFewColoursComesBackExactly(int width, int height) {
    var encoder = G2mVideoEncoder.Create(_Stream(width, height));
    var decoder = G2mVideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 6; ++i) {
      var picture = _Screen(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  [TestCase(".avi", 64, 48)]
  [TestCase(".mkv", 150, 97)]
  [TestCase(".avi", 320, 240)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = G2mVideoEncoder.Create(_Stream(width, height));
    var decoder = G2mVideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 16; ++i) {
      var picture = i == 7 ? _Noise(width, height, i) : _Screen(width, height, i % 5 == 4 ? i - 1 : i);
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
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "rgb24");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>G2M_SAMPLES</c> at films from <c>samples.ffmpeg.org/V-codecs/G2M2</c>, <c>G2M3</c> and <c>G2M4</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real Go2Meeting films; point G2M_SAMPLES at them.")]
  public void EveryPictureOfARealG2mFilmMatchesFFmpeg()
    => RealSampleComparison.Run("G2M_SAMPLES", G2mVideoDecoder.Accepts, "rgb24");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("G2M4"),
    Handler = CodecTag.FromCharacters("G2M4"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 10),
    FrameRate = new Rational(10, 1),
  };

  private static RawImage _Screen(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 4 + step * 3 && x < 40 + step * 3 && y >= 5 + step && y < 30 + step;
      var stripe = (x / 9 + y / 7) % 4;
      pixels[at] = inside ? (byte)250 : (byte)(30 + 50 * stripe);
      pixels[at + 1] = inside ? (byte)(x % 3 * 60) : (byte)(80 + 20 * stripe);
      pixels[at + 2] = inside ? (byte)20 : (byte)200;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Noise(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    random.NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
