using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>The MPEG-2 encoder, on streams written here and read back through the public decoder.</summary>
/// <remarks>
/// The external conformance check belongs to <c>EncoderOracleTests</c>: the encoder is marked
/// <see cref="VerifiedByAttribute"/> for ffmpeg, so that fixture writes this codec through a real
/// container and asks an independent decoder to open it. These unit tests keep the local invariants
/// cheap and exact: declared profile/level geometry, the start-code shape, registration, refusals and
/// the fact that a non-macroblock-sized picture survives this encoder and this decoder as the same
/// visible picture rather than as its padded coded dimensions.
/// </remarks>
[TestFixture]
public sealed class Mpeg2VideoEncoderTests {

  [TestCase(16, 16, 25, 1)]
  [TestCase(720, 576, 25, 1)]
  [TestCase(720, 480, 30, 1)]
  [TestCase(352, 288, 24_000, 1_001)]
  [Category("Unit")]
  public void MainProfileAtMainLevelGeometryIsAccepted(int width, int height, long rateNumerator, long rateDenominator) {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height, new(rateNumerator, rateDenominator)));
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Width, Is.EqualTo(width));
      Assert.That(described.Height, Is.EqualTo(height));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(rateNumerator, rateDenominator)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
    });
  }

  [TestCase(721, 576, 25, 1)]
  [TestCase(720, 577, 25, 1)]
  [TestCase(720, 576, 30, 1)]
  [TestCase(17, 16, 25, 1)]
  [TestCase(16, 17, 25, 1)]
  [TestCase(0, 16, 25, 1)]
  [Category("Unit")]
  public void GeometryOutsideTheDeclaredLevelOrFourTwoZeroGridIsRefused(
    int width, int height, long rateNumerator, long rateDenominator) {
    var refusal = Assert.Throws<NotSupportedException>(
      () => Mpeg2VideoEncoder.Create(_Stream(width, height, new(rateNumerator, rateDenominator))));

    Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
  }

  [TestCase(50, 1)]
  [TestCase(60_000, 1_001)]
  [TestCase(15, 1)]
  [Category("Unit")]
  public void FrameRatesThisMainLevelWriterDoesNotSignalAreRefused(long numerator, long denominator) {
    var refusal = Assert.Throws<NotSupportedException>(
      () => Mpeg2VideoEncoder.Create(_Stream(352, 288, new(numerator, denominator))));

    Assert.That(refusal!.Message, Does.Contain($"{numerator}/{denominator}"));
  }

  [Test]
  [Category("Unit")]
  public void ThePacketCarriesTheHeadersThatMakeItMpeg2AndIndependentlyDecodable() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 16));
    Assert.That(encoder.TryEncode(_Picture(32, 16), 7, out var packet), Is.True);

    var data = packet.Data.Span;
    Assert.Multiple(() => {
      Assert.That(data[..4].ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceHeader }));
      Assert.That(_ContainsStartCode(data, MpegStartCode.Extension), Is.True);
      Assert.That(_ContainsStartCode(data, MpegStartCode.Picture), Is.True);
      Assert.That(_ContainsStartCode(data, MpegStartCode.FirstSlice), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVisibleSizeThatIsNotWholeMacroblocksRoundTripsAtItsDeclaredSize() {
    const int width = 18;
    const int height = 34;

    var source = _Picture(width, height);
    var encoder = Mpeg2VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var decoder = Mpeg2VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out _), Is.False, "the first I picture is the anchor held until flush");
    var decoded = decoder.Flush().Single();

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(_MeanSquaredError(source.PixelData, decoded.PixelData), Is.LessThan(400d));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryPictureIsAKeyFrameAndNothingIsHeldByTheEncoder() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 32));

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_Picture(32, 32, index), index, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True);
    }

    Assert.That(encoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefused() {
    var encoder = Mpeg2VideoEncoder.Create(_Stream(32, 32));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(34, 32), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("34x32"));
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescriptionIsAcceptedByTheDecoderAndTheRegistryReachesBoth() {
    var stream = _Stream(32, 32);
    var described = Mpeg2VideoEncoder.Create(stream).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("MPG2")));
      Assert.That(described.CodecId, Is.EqualTo("V_MPEG2"));
      Assert.That(Mpeg2VideoDecoder.Accepts(described), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(described), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(described), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(described), Is.InstanceOf<Mpeg2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(described), Is.InstanceOf<Mpeg2VideoEncoder>());
    });
  }

  private static MediaStreamInfo _Stream(int width, int height, Rational? frameRate = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MPG2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = frameRate ?? new Rational(25, 1),
  };

  private static RawImage _Picture(int width, int height, int phase = 0) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x * 7 + phase * 13) & 0xFF);
        pixels[at + 1] = (byte)((y * 9 + phase * 5) & 0xFF);
        pixels[at + 2] = (byte)(((x + y) * 5 + phase * 17) & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static bool _ContainsStartCode(ReadOnlySpan<byte> data, byte code) {
    for (var offset = 0; offset + 3 < data.Length; ++offset)
      if (data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 1 && data[offset + 3] == code)
        return true;

    return false;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));

    double sum = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = expected[i] - actual[i];
      sum += delta * delta;
    }

    return sum / expected.Length;
  }
}
