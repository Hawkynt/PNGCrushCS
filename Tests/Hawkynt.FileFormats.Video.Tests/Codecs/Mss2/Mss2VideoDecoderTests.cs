using System;
using System.Buffers.Binary;
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
/// MS Screen 2: registration, the run-length encoder against the decoder and against FFmpeg, and
/// hand-built 15-bit frames against FFmpeg.
/// </summary>
/// <remarks>
/// The region coder, the WMV9 rectangles — implicit, listed, masked, range reduced and at half
/// resolution — the picture-wide motion vector and the two-slice split are checked by the opt-in
/// real-film comparison: the four films of <c>samples.ffmpeg.org/V-codecs/MSS2</c>, 1,438 pictures,
/// every one identical to FFmpeg's. None of them uses the run-length or 15-bit modes, which is what
/// the tests here cover.
/// </remarks>
[TestFixture]
public sealed class Mss2VideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void Mss2HasADecoderAndAnEncoderOfItsOwn() {
    var stream = Mss2VideoEncoder.Create(_Stream(64, 48)).DescribeStream();
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Mss2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(_Stream(64, 48)), Is.InstanceOf<Mss2VideoEncoder>());
      Assert.That(Mss1VideoDecoder.Accepts(stream), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithoutItsExtradataIsRefused()
    => Assert.Throws<InvalidDataException>(() => Mss2VideoDecoder.Create(_Stream(64, 48)));

  [TestCase(64, 48)]
  [TestCase(101, 37)]
  [Category("Unit")]
  public void APictureOfFewColoursComesBackExactly(int width, int height) {
    var encoder = Mss2VideoEncoder.Create(_Stream(width, height));
    var decoder = Mss2VideoDecoder.Create(encoder.DescribeStream());
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
  [TestCase(".mkv", 101, 37)]
  [TestCase(".avi", 320, 240)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = Mss2VideoEncoder.Create(_Stream(width, height));
    var decoder = Mss2VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 20; ++i) {
      var picture = i == 9 ? _Noise(width, height, i) : _Screen(width, height, i % 7 == 6 ? i - 1 : i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData);
    }

    _AssertFFmpegAgrees(encoder.DescribeStream(), packets, ours, extension);
  }

  /// <summary>Hand-built 15-bit frames: literals, runs, copies from the row above, and an inter frame that skips.</summary>
  [Test]
  [Category("Conformance")]
  public void FifteenBitFramesDecodeToFFmpegsPictures() {
    FFmpegOracle.RequireAvailable();
    const int width = 24, height = 10;
    var stream = _FifteenBitStream(width, height);
    var key = new List<byte> { 0x80, 0x60 };
    for (var y = 0; y < height; ++y) {
      if (y % 3 == 2) {
        key.Add(128); // the row above
        key.Add(131); // then one byte of run: repeat this many times more
        key.Add((byte)(width - 3));
        key.Add(0x00);
        key.Add(0x1F); // a literal
        continue;
      }

      for (var x = 0; x < width; ++x) {
        var value = ((x & 31) << 10) | ((y * 3 & 31) << 5) | ((x + y) & 31);
        key.Add((byte)(value >> 8));
        key.Add((byte)value);
      }
    }

    // Inter frame: rectangle (2, 1) to (9, 4), skip four pixels, then a run of one colour.
    var inter = new List<byte> { 0x18, 0x00, 0x20, 0x09, 0x00, 0x10, 0x04, 129, 130 + 1, 2, 0x7C, 0x00, 131, 20 };

    var decoder = Mss2VideoDecoder.Create(stream);
    var packets = new List<CodedPacket> { new(0, key.ToArray(), PresentationTimestamp: 0, IsKeyFrame: true), new(0, inter.ToArray(), PresentationTimestamp: 1) };
    var ours = new List<byte[]>();
    foreach (var packet in packets) {
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData);
    }

    _AssertFFmpegAgrees(stream, packets, ours, ".avi");
  }

  /// <summary>Opt-in: point <c>MSS2_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/MSS2</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real MS Screen 2 films; point MSS2_SAMPLES at them.")]
  public void EveryPictureOfARealMss2FilmMatchesFFmpeg()
    => RealSampleComparison.Run("MSS2_SAMPLES", Mss2VideoDecoder.Accepts, "rgb24");

  // ==========================================================================================

  private static void _AssertFFmpegAgrees(MediaStreamInfo stream, List<CodedPacket> packets, List<byte[]> ours, string extension) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      File.WriteAllBytes(path, extension == ".avi"
        ? VideoIO.Mux<AviWriter>([stream], packets)
        : VideoIO.Mux<MatroskaWriter>([stream], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "rgb24");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  private static MediaStreamInfo _FifteenBitStream(int width, int height) {
    var description = Mss2VideoEncoder.Create(_Stream(width, height)).DescribeStream();
    var format = description.CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(format.AsSpan(40 + 48), 127); // 127 changeable entries: a 15-bit stream
    return new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = description.Codec, Handler = description.Codec,
      CodecId = description.CodecId, TimeBase = description.TimeBase, FrameRate = description.FrameRate,
      Width = width, Height = height, BitsPerPixel = 24, CodecPrivateData = format,
    };
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MSS2"),
    Handler = CodecTag.FromCharacters("MSS2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
  };

  /// <summary>A screen-like picture of a few flat colours with a window that moves.</summary>
  private static RawImage _Screen(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 4 + step * 2 && x < 30 + step * 2 && y >= 5 + step && y < 25 + step;
      var stripe = (x / 8 + y / 6) % 3;
      pixels[at] = inside ? (byte)255 : (byte)(40 + 60 * stripe);
      pixels[at + 1] = inside ? (byte)255 : (byte)(90 + 30 * stripe);
      pixels[at + 2] = inside ? (byte)(y % 2 == 0 ? 0 : 128) : (byte)160;
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
