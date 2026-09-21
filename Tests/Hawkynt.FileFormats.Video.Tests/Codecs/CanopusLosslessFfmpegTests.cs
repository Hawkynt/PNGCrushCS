using System;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Hands FFmpeg what this package writes as CLLC and requires the samples back unchanged.
/// </summary>
/// <remarks>
/// CLLC is lossless, so the only defensible oracle assertion is equality. A tolerance would be a
/// statement about how wrong the codec is allowed to be, and the answer for a lossless codec is
/// "not at all".
/// <para/>
/// Equality is also the only assertion that can catch the failure this pairing is prone to. Encoder
/// and decoder here share a channel order, a prediction rule and a bit reader; get any of them
/// consistently wrong and the round-trip test in
/// <see cref="CanopusLosslessCodecTests"/> still passes, because both halves are wrong the same
/// way. So does a check that merely counts frames or measures the picture: a frame with red and
/// blue exchanged is exactly the right size. Only a third implementation that never saw this one
/// can see it, and only if the comparison is over samples.
/// <para/>
/// Each coding mode is asked for in its own native layout rather than through RGB24, because CLLC
/// type 0 stores subsampled YUV and the trip out through a colour matrix would destroy the very
/// exactness being asserted. All three modes the writer emits are covered — type 0 YUV 4:2:2,
/// type 1 RGB24 and type 3 ARGB — over a picture large enough that the horizontal prediction chain
/// and the row-to-row top-left carry both run long.
/// </remarks>
[TestFixture]
public sealed class CanopusLosslessFfmpegTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 48;

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsNativeYuv422SampleForSample() {
    FFmpegOracle.RequireAvailable();

    var lumaLength = _WIDTH * _HEIGHT;
    var chromaLength = _WIDTH / 2 * _HEIGHT;
    var samples = new byte[lumaLength + 2 * chromaLength];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x)
      samples[y * _WIDTH + x] = (byte)(16 + (x * 7 + y * 29 + x * y) % 220);
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH / 2; ++x) {
      samples[lumaLength + y * (_WIDTH / 2) + x] = (byte)(16 + (x * 37 + y * 11) % 225);
      samples[lumaLength + chromaLength + y * (_WIDTH / 2) + x] = (byte)(240 - (x * 53 + y * 19) % 225);
    }

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(16));
    Assert.That(encoder.TryEncode(new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Yuv422P8,
      PixelData = samples,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    }, 0, out var packet), Is.True);

    Assert.That(packet.Data.Span[1], Is.EqualTo(0), "type 0 is the YUV 4:2:2 coding this asserts about.");
    _AssertFfmpegAgrees(encoder.DescribeStream(), packet, "yuv422p", samples);
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsRgb24SampleForSample() {
    FFmpegOracle.RequireAvailable();

    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * 3;

      // Deliberately unequal channels: a writer that exchanged red and blue would round-trip
      // through this package's own decoder and be caught only here.
      pixels[at] = (byte)(x * 4 & 0xFF);
      pixels[at + 1] = (byte)(y * 5 & 0xFF);
      pixels[at + 2] = (byte)((x * 13 + y * 31) & 0xFF);
    }

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(24));
    Assert.That(encoder.TryEncode(new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }, 0, out var packet), Is.True);

    Assert.That(packet.Data.Span[1], Is.EqualTo(1), "type 1 is the RGB24 coding this asserts about.");
    _AssertFfmpegAgrees(encoder.DescribeStream(), packet, "rgb24", pixels);
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsCodingTypeTwoWithTheSamePayloadSyntaxAsTypeOne() {
    FFmpegOracle.RequireAvailable();

    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i * 37 + i / 97) & 0xFF);

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(24));
    Assert.That(encoder.TryEncode(new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }, 0, out var packet), Is.True);

    // The writer only ever emits type 1, so nothing else in the suite can say whether the reader is
    // right to treat type 2 as the same payload. Relabelling a type 1 frame and asking the
    // reference decoder settles it against something other than this package's own belief.
    var relabelled = packet.Data.ToArray();
    relabelled[1] = 2;

    _AssertFfmpegAgrees(encoder.DescribeStream(), new(0, relabelled, IsKeyFrame: true), "rgb24", pixels);
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsArgbSampleForSample() {
    FFmpegOracle.RequireAvailable();

    var pixels = new byte[_WIDTH * _HEIGHT * 4];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * 4;

      // Transparent pixels are scattered through the picture on purpose: CLLC omits the three
      // colour symbols under alpha zero, so the entropy stream desynchronises for every later
      // pixel in the frame if the writer and the reference decoder disagree about when to skip.
      var alpha = (byte)((x + y) % 7 == 0 ? 0 : 32 + (x * 29 + y * 47) % 224);
      pixels[at] = alpha;
      if (alpha == 0)
        continue;

      pixels[at + 1] = (byte)(x * 3 & 0xFF);
      pixels[at + 2] = (byte)(y * 5 & 0xFF);
      pixels[at + 3] = (byte)((x * 43 + y * 71) & 0xFF);
    }

    var encoder = CanopusLosslessVideoEncoder.Create(_Stream(32));
    Assert.That(encoder.TryEncode(new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Argb32,
      PixelData = pixels,
    }, 0, out var packet), Is.True);

    Assert.That(packet.Data.Span[1], Is.EqualTo(3), "type 3 is the ARGB coding this asserts about.");
    _AssertFfmpegAgrees(encoder.DescribeStream(), packet, "argb", pixels);
  }

  private static void _AssertFfmpegAgrees(
    MediaStreamInfo stream, CodedPacket packet, string pixelFormat, byte[] expected) {
    var avi = VideoIO.Mux<AviWriter>([stream], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail, pictures) =
        FFmpegOracle.TryDecodePicturesAs(path, _WIDTH, _HEIGHT, 1, pixelFormat, expected.Length);
      Assert.That(decoded, Is.True, detail);

      for (var i = 0; i < expected.Length; ++i)
        if (pictures[i] != expected[i])
          Assert.Fail(
            $"ffmpeg decoded {pixelFormat} sample {i} as {pictures[i]} where {expected[i]} was encoded; "
            + $"{_Disagreements(expected, pictures)} of {expected.Length} samples differ.");
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static int _Disagreements(byte[] expected, byte[] actual) {
    var count = 0;
    for (var i = 0; i < expected.Length; ++i)
      if (expected[i] != actual[i])
        ++count;
    return count;
  }

  private static MediaStreamInfo _Stream(int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CLLC"),
    Handler = CodecTag.FromCharacters("CLLC"),
    Width = _WIDTH,
    Height = _HEIGHT,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
