using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Asv2.Tests;

/// <summary>
/// The ASV2 encoder, checked against the decoder beside it and against the bits it writes.
/// </summary>
/// <remarks>
/// The coding is lossy — a discrete cosine transform and one quantiser for the whole file — so "the
/// picture comes back" is a contract only for what the format can hold exactly, which is a flat one.
/// Unlike ASV1 there are no positions the coding cannot reach: ASV2 states its coefficient-group
/// count up front and so carries all sixteen groups.
/// <para/>
/// <b>Measured against ffmpeg.</b> Ten streams — 34x18 to 352x288, four of them not a whole number of
/// macroblocks, and content from a flat colour through <c>testsrc2</c>, <c>mandelbrot</c>,
/// <c>smptebars</c>, <c>rgbtestsrc</c> and a gradient to uniform noise — 82 frames in all, were muxed
/// into AVIs and decoded by ffmpeg 9.0.1. See this codec's section of <c>README.md</c> for the
/// numbers; what these tests add is the shape of the bytes, which a comparison against another
/// decoder cannot see at all.
/// </remarks>
[TestFixture]
public sealed class Asv2VideoEncoderTests {

  /// <summary>The quantiser the encoder writes, which the private data has to state.</summary>
  private const int _QUANTISER = 16;

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = Asv2VideoEncoder.Create(_Requested(48, 32));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("ASV2")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("ASV2")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(48));
      Assert.That(stream.Height, Is.EqualTo(32));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(48), "BITMAPINFOHEADER plus the eight-byte global header");
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(40), "biSize");
      Assert.That(format[16..20], Is.EqualTo("ASV2"u8.ToArray()), "biCompression");
      Assert.That(format[40], Is.EqualTo(_QUANTISER), "the one quantisation parameter of asv1.txt 4.2");
      Assert.That(format[44..48], Is.EqualTo("ASUS"u8.ToArray()), "the tag the reference encoder writes behind it");
    });

    Assert.That(Asv2VideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Asv2VideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheCodeItWrites() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("ASUS V2"));

    var stream = _Requested(16, 16);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Asv2VideoEncoder>());
  }

  // ============================================================================================
  // What the bitstream looks like
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatPictureIsCodedAsOneEmptyCoefficientGroupAndADirectCurrentField() {
    // Fourteen bits a block — four of coefficient-group count, eight of direct current and the two of
    // the first group's own empty pattern — six blocks to the one macroblock, so eighty-four bits.
    var packet = _Encode(16, 16, _Flat(16, 16, 128, 128, 128));

    Assert.That(packet.Data.Length, Is.EqualTo(11));

    var reader = new H263BitReader(Asv2Bitstream.ReverseBits(packet.Data.Span));
    for (var block = 0; block < 6; ++block) {
      Assert.That(Asv2Bitstream.ReadReversedBits(ref reader, 4), Is.EqualTo(0), $"block {block} coefficient groups");
      Assert.That(Asv2Bitstream.ReadReversedBits(ref reader, 8), Is.EqualTo(block < 4 ? 126 : 128), $"block {block}");
      Assert.That(Asv2VlcTables.FirstCoefficientPattern.Read(ref reader), Is.EqualTo(0));
    }
  }

  [Test]
  [Category("Unit")]
  public void AHardVerticalEdgeIsCodedWithAnEscapedLevel() {
    // A step from black to white halfway across an eight-sample row puts everything into the
    // horizontal frequencies and nothing into the vertical ones, so the first coefficient group holds
    // exactly one coefficient — the one at raster position one — and it is far outside the thirty-one
    // magnitudes the level table can state.
    var packet = _Encode(16, 16, _VerticalEdge(16, 16));
    var reader = new H263BitReader(Asv2Bitstream.ReverseBits(packet.Data.Span));

    Assert.That(Asv2Bitstream.ReadReversedBits(ref reader, 4), Is.GreaterThan(0), "the block holds more than one group");
    Asv2Bitstream.ReadReversedBits(ref reader, 8);

    Assert.That(Asv2VlcTables.FirstCoefficientPattern.Read(ref reader), Is.EqualTo(0b0010),
      "only the position the first group's third pattern bit names carries a coefficient");

    var level = Asv2VlcTables.ReadLevel(ref reader);
    Assert.That(Math.Abs(level), Is.GreaterThan(31),
      "the level table states magnitudes one to thirty-one, so anything beyond took the eight-bit escape");
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameAndNothingIsHeldBack() {
    var encoder = Asv2VideoEncoder.Create(_Requested(16, 16));
    var picture = _Flat(16, 16, 40, 90, 200);

    for (var i = 0; i < 3; ++i) {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True, "ASV2 has no prediction between pictures");
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(i));
    }

    Assert.That(encoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void TheSameInputProducesTheSameBytes() {
    var picture = _VerticalEdge(32, 32);
    var first = _Encode(32, 32, picture).Data.ToArray();
    var second = _Encode(32, 32, picture).Data.ToArray();

    Assert.That(second, Is.EqualTo(first));
  }

  [Test]
  [Category("Unit")]
  public void TheHighestCoefficientGroupsAreReachedWhereAsv1CannotReachThem() {
    // The one place the two codings genuinely differ in what they can express: a checkerboard puts its
    // whole energy in the last coefficient group, which ASV1 states must be nought and ASV2 carries.
    var packet = _Encode(16, 16, _Checkerboard(16, 16));
    var reader = new H263BitReader(Asv2Bitstream.ReverseBits(packet.Data.Span));

    Assert.That(Asv2Bitstream.ReadReversedBits(ref reader, 4), Is.EqualTo(15),
      "a block whose loudest coefficient is the highest frequency states all sixteen coefficient groups");
  }

  // ============================================================================================
  // What comes back
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatGreyPictureComesBackExactly([Values(0, 1, 17, 128, 199, 255)] int level) {
    var picture = _Flat(32, 32, (byte)level, (byte)level, (byte)level);

    Assert.That(_RoundTrip(32, 32, picture).PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfMacroblocksComesBackFlat() {
    var picture = _Flat(34, 18, 128, 128, 128);

    Assert.That(_RoundTrip(34, 18, picture).PixelData, Is.EqualTo(picture.PixelData));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefused() {
    var encoder = Asv2VideoEncoder.Create(_Requested(16, 16));
    var failure = Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_Flat(32, 32, 0, 0, 0), 0, out _));

    Assert.That(failure.Message, Does.Contain("32x32"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(
      () => Asv2VideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Audio, Width = 16, Height = 16 }));

    Assert.That(failure.Message, Does.Contain("video stream"));
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  public void APictureSizeTheContainerDidNotStateIsRefused(int width, int height) {
    var failure = Assert.Throws<NotSupportedException>(() => Asv2VideoEncoder.Create(_Requested(width, height)));

    Assert.That(failure.Message, Does.Contain("picture size"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Requested(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("ASV2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static CodedPacket _Encode(int width, int height, RawImage picture) {
    var encoder = Asv2VideoEncoder.Create(_Requested(width, height));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    return packet;
  }

  private static RawImage _RoundTrip(int width, int height, RawImage picture) {
    var encoder = Asv2VideoEncoder.Create(_Requested(width, height));
    var decoder = Asv2VideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    return frame;
  }

  private static RawImage _Flat(int width, int height, byte red, byte green, byte blue) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>Black for the left four columns of every eight, white for the right four.</summary>
  private static RawImage _VerticalEdge(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)((x & 4) != 0 ? 255 : 0);
        var at = (y * width + x) * 3;
        pixels[at] = value;
        pixels[at + 1] = value;
        pixels[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>The highest frequency a block can hold: black and white alternating every sample.</summary>
  private static RawImage _Checkerboard(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)(((x ^ y) & 1) != 0 ? 255 : 0);
        var at = (y * width + x) * 3;
        pixels[at] = value;
        pixels[at + 1] = value;
        pixels[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
