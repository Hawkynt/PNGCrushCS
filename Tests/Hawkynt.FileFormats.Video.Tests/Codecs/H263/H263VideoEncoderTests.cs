using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>The H.263 encoder, checked against the hand-built syntax fixtures and this package's decoder.</summary>
/// <remarks>
/// The exact flat-picture test is intentionally stronger than an ordinary round trip: its expected
/// bytes come from <see cref="H263TestStream"/>, which writes the Recommendation's fields and codewords
/// directly rather than calling the encoder's helpers. FFmpeg remains the independent interoperability
/// oracle for the table transcription and packet as a whole.
/// </remarks>
[TestFixture]
public sealed class H263VideoEncoderTests {

  private const int _SUB_QCIF_MACROBLOCKS = 8 * 6;

  [TestCase(128, 96)]
  [TestCase(176, 144)]
  [TestCase(352, 288)]
  [TestCase(704, 576)]
  [TestCase(1408, 1152)]
  [Category("Unit")]
  public void TheFiveStandardSourceFormatsAreAccepted(int width, int height) {
    var encoder = H263VideoEncoder.Create(_Stream(width, height));

    Assert.Multiple(() => {
      Assert.That(encoder.DescribeStream().Width, Is.EqualTo(width));
      Assert.That(encoder.DescribeStream().Height, Is.EqualTo(height));
    });
  }

  [TestCase(64, 48)]
  [TestCase(160, 120)]
  [TestCase(320, 240)]
  [TestCase(720, 576)]
  [TestCase(0, 0)]
  [Category("Unit")]
  public void ACustomPictureSizeIsRefusedByName(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => H263VideoEncoder.Create(_Stream(width, height)));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
      Assert.That(refusal.Message, Does.Contain("extended PTYPE"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => H263VideoEncoder.Create(
      new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("H263"), Width = 176, Height = 144 }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfADifferentSizeFromTheStreamIsRefused() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(176, 144, 128, 128), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("176x144"));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureIsExactlyTheBitstreamTheRecommendationDescribes() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    Assert.That(encoder.TryEncode(_Flat(128, 96, 128, 128), 0, out var packet), Is.True);

    var expected = new H263TestStream()
      .PictureHeader(sourceFormat: 1, quantiser: 8)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, 255)
      .ToArray();

    Assert.That(packet.Data.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureComesBackExactly() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    Assert.That(encoder.TryEncode(_Flat(128, 96, 200, 128), 7, out var packet), Is.True);

    var planes = _DecodePlanes(packet, 128, 96);
    Assert.Multiple(() => {
      Assert.That(planes.Take(128 * 96).Distinct().ToArray(), Is.EqualTo(new byte[] { 200 }));
      Assert.That(planes.Skip(128 * 96).Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void AlternatingCurrentCoefficientsRoundTripThroughTheDecoder() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));
    var source = _Picture(128, 96);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoded = _DecodePlanes(packet, 128, 96);
    var error = _MeanSquaredError(source.PixelData, decoded);

    Assert.That(error, Is.LessThan(64d), $"the intra picture came back {error:F1} squared levels from its source");
  }

  [Test]
  [Category("Unit")]
  public void SuccessivePicturesAdvanceTheTemporalReference() {
    var encoder = H263VideoEncoder.Create(_Stream(128, 96));

    for (var index = 0; index < 2; ++index) {
      encoder.TryEncode(_Flat(128, 96, 128, 128), index, out var packet);
      var reader = new H263BitReader(packet.Data.Span);
      Assert.That(reader.ReadBits(22), Is.EqualTo(1));
      var header = H263PictureHeader.Parse(ref reader);
      Assert.That(header.TemporalReference, Is.EqualTo(index), $"picture {index}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesAStreamTheDecoderAccepts() {
    var encoder = H263VideoEncoder.Create(_Stream(176, 144));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("H263")));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(0));
      Assert.That(H263VideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfTheCodec() {
    var stream = _Stream(176, 144);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<H263VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<H263VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBackAtTheEnd()
    => Assert.That(H263VideoEncoder.Create(_Stream(128, 96)).Flush(), Is.Empty);

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H263"),
    Width = width,
    Height = height,
  };

  private static RawImage _Flat(int width, int height, byte luminance, byte chrominance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(chrominance);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static RawImage _Picture(int width, int height) {
    var chromaWidth = width / 2;
    var chromaHeight = height / 2;
    var planes = new byte[width * height * 3 / 2];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        planes[y * width + x] = (byte)(24 + ((x * 7 + y * 11 + (x ^ y) * 3) % 208));

    for (var y = 0; y < chromaHeight; ++y)
      for (var x = 0; x < chromaWidth; ++x) {
        planes[width * height + y * chromaWidth + x] = (byte)(80 + (x * 5 + y * 3) % 96);
        planes[width * height + chromaWidth * chromaHeight + y * chromaWidth + x] =
          (byte)(80 + (x * 2 + y * 7) % 96);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static byte[] _DecodePlanes(CodedPacket packet, int width, int height) {
    var reader = new H263BitReader(packet.Data.Span);
    Assert.That(reader.ReadBits(22), Is.EqualTo(1), "picture start code");

    var header = H263PictureHeader.Parse(ref reader);
    var picture = H263PictureDecoder.BeginPicture(
      header, new(header.MacroblockWidth, header.MacroblockHeight), reference: null);
    picture.DecodePicture(ref reader);

    var lumaSamples = width * height;
    var chromaSamples = lumaSamples / 4;
    var result = new byte[lumaSamples + 2 * chromaSamples];
    Array.Copy(picture.Target.Luma, result, lumaSamples);
    Array.Copy(picture.Target.Cb, 0, result, lumaSamples, chromaSamples);
    Array.Copy(picture.Target.Cr, 0, result, lumaSamples + chromaSamples, chromaSamples);
    return result;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));

    long squared = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      squared += difference * difference;
    }

    return (double)squared / expected.Length;
  }
}
