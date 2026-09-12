using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>AVUI packets built independently at the byte offsets the reference decoder consumes.</summary>
[TestFixture]
public class AvuiVideoDecoderTests {

  private static readonly CodecTag _Avui = CodecTag.FromCharacters("AVUI");
  private const int _Width = 720;

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    CodecTag? codec = null,
    int depth = AvuiVideoFormat.OpaqueDepth,
    ReadOnlyMemory<byte> privateData = default) => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = codec ?? _Avui,
      Width = width,
      Height = height,
      BitsPerPixel = depth,
      CodecPrivateData = privateData,
    };

  private static byte[] _Aprg(bool interlaced) {
    var result = new byte[24];
    result[3] = 24;
    "APRGAPRG0001"u8.CopyTo(result.AsSpan(4));
    result[19] = interlaced ? (byte)2 : (byte)1;
    return result;
  }

  [Test]
  [Category("Unit")]
  public void AcceptsTheAvuiTagIgnoringCase() {
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(_Width, 486)), Is.True);
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(_Width, 486, CodecTag.FromCharacters("avui"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(_Width, 486, CodecTag.FromCharacters("avrp"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => AvuiVideoDecoder.Create(_Stream(0, 486)));
    Assert.That(failure!.Message, Does.Contain("0x486"));
  }

  [TestCase(_Width, 486)]
  [TestCase(_Width, 576)]
  [Category("Unit")]
  public void AcceptsTheTwoD1Geometries(int width, int height) {
    Assert.That(() => AvuiVideoDecoder.Create(_Stream(width, height)), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryOutsideTheTwoD1Standards() {
    var failure = Assert.Throws<NotSupportedException>(() => AvuiVideoDecoder.Create(_Stream(_Width, 480)));
    Assert.That(failure!.Message, Does.Contain("720x480"));
  }

  [Test]
  [Category("Unit")]
  public void ProgressiveNtscStartsAfterTwoHalfBlankingRuns() {
    const int skip = 10;
    var packet = new byte[2 * _Width * (486 + skip)];
    var firstRow = 2 * _Width * skip;
    new byte[] { 201, 101, 151, 102, 203, 103, 153, 104 }.CopyTo(packet, firstRow);

    var decoder = AvuiVideoDecoder.Create(_Stream(_Width, 486, privateData: _Aprg(interlaced: false)));
    var (luma, cb, cr) = decoder.DecodePlanes(packet);

    Assert.Multiple(() => {
      Assert.That(luma[..4], Is.EqualTo(new byte[] { 101, 102, 103, 104 }));
      Assert.That(cb[..2], Is.EqualTo(new byte[] { 201, 203 }));
      Assert.That(cr[..2], Is.EqualTo(new byte[] { 151, 153 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void InterlacedNtscStoresTheOddFieldBeforeTheEvenField() {
    const int height = 486;
    const int halfBlank = _Width * 10;
    const int fieldPayload = _Width * height;
    var opaqueLength = 2 * _Width * (height + 10) + 4;
    var packet = new byte[opaqueLength];

    // Field zero is displayed on row 1 for 486-line NTSC.
    new byte[] { 21, 11, 31, 12 }.CopyTo(packet, halfBlank);
    // Field one follows field zero's whole payload, a four-byte separator, and its own blanking.
    new byte[] { 61, 51, 71, 52 }.CopyTo(packet, halfBlank + fieldPayload + 4 + halfBlank);

    var decoder = AvuiVideoDecoder.Create(_Stream(_Width, height, privateData: _Aprg(interlaced: true)));
    var (luma, cb, cr) = decoder.DecodePlanes(packet);

    Assert.Multiple(() => {
      Assert.That(luma[0..2], Is.EqualTo(new byte[] { 51, 52 }), "even row comes from the second coded field");
      Assert.That(luma[_Width..(_Width + 2)], Is.EqualTo(new byte[] { 11, 12 }), "odd row comes from the first coded field");
      Assert.That(cb[0], Is.EqualTo(61));
      Assert.That(cb[_Width / 2], Is.EqualTo(21));
      Assert.That(cr[0], Is.EqualTo(71));
      Assert.That(cr[_Width / 2], Is.EqualTo(31));
    });
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPalStoresTheEvenFieldBeforeTheOddField() {
    const int height = 576;
    const int halfBlank = _Width * 16;
    const int fieldPayload = _Width * height;
    var packet = new byte[2 * _Width * (height + 16) + 4];
    new byte[] { 21, 11, 31, 12 }.CopyTo(packet, halfBlank);
    new byte[] { 61, 51, 71, 52 }.CopyTo(packet, halfBlank + fieldPayload + 4 + halfBlank);

    var decoder = AvuiVideoDecoder.Create(_Stream(_Width, height, privateData: _Aprg(interlaced: true)));
    var (luma, _, _) = decoder.DecodePlanes(packet);

    Assert.Multiple(() => {
      Assert.That(luma[0..2], Is.EqualTo(new byte[] { 11, 12 }));
      Assert.That(luma[_Width..(_Width + 2)], Is.EqualTo(new byte[] { 51, 52 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void MissingAprgUsesTheReferenceDecodersInterlacedDefault() {
    const int height = 486;
    const int halfBlank = _Width * 10;
    var packet = new byte[2 * _Width * (height + 10) + 4];
    new byte[] { 21, 11, 31, 12 }.CopyTo(packet, halfBlank);

    var decoder = AvuiVideoDecoder.Create(_Stream(_Width, height));
    var (luma, _, _) = decoder.DecodePlanes(packet);

    Assert.That(luma[_Width..(_Width + 2)], Is.EqualTo(new byte[] { 11, 12 }));
  }

  [Test]
  [Category("Unit")]
  public void Depth32ReadsTheInvertedAlphaCompanion() {
    const int height = 486;
    const int skip = 10;
    var opaqueLength = 2 * _Width * (height + skip);
    var packet = new byte[2 * opaqueLength + 4];
    var colour = 2 * _Width * skip;
    new byte[] { 128, 16, 128, 16 }.CopyTo(packet, colour);

    var alpha = opaqueLength + 5 + 2 * _Width * skip;
    packet[alpha] = 255;     // stored inverted: decoded alpha 0
    packet[alpha + 2] = 127; // decoded alpha 128

    var decoder = AvuiVideoDecoder.Create(_Stream(
      _Width, height, depth: AvuiVideoFormat.AlphaDepth, privateData: _Aprg(interlaced: false)));
    var (_, _, _, decodedAlpha) = decoder.DecodePlanesWithAlpha(packet);

    Assert.That(decodedAlpha, Is.Not.Null);
    Assert.That(decodedAlpha![..2], Is.EqualTo(new byte[] { 0, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void Depth32ReturnsRgbaRatherThanDroppingAlpha() {
    const int height = 486;
    const int skip = 10;
    var opaqueLength = 2 * _Width * (height + skip);
    var packet = new byte[2 * opaqueLength + 4];
    packet[opaqueLength + 5 + 2 * _Width * skip] = 255;

    var decoder = AvuiVideoDecoder.Create(_Stream(
      _Width, height, depth: AvuiVideoFormat.AlphaDepth, privateData: _Aprg(interlaced: false)));
    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.True);
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(frame.PixelData[3], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void Depth32RefusesATruncatedAlphaCompanion() {
    const int height = 486;
    var opaqueLength = 2 * _Width * (height + 10);
    var decoder = AvuiVideoDecoder.Create(_Stream(
      _Width, height, depth: AvuiVideoFormat.AlphaDepth, privateData: _Aprg(interlaced: false)));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[2 * opaqueLength + 3]));
    Assert.That(failure!.Message, Does.Contain("colour and alpha"));
  }

  [Test]
  [Category("Unit")]
  public void OpaqueTryDecodeKeepsTheExistingRgb24Contract() {
    const int height = 486;
    var packet = new byte[2 * _Width * (height + 10)];
    var decoder = AvuiVideoDecoder.Create(_Stream(_Width, height, privateData: _Aprg(interlaced: false)));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.True);
      Assert.That(frame.Width, Is.EqualTo(_Width));
      Assert.That(frame.Height, Is.EqualTo(height));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData.Length, Is.EqualTo(_Width * height * 3));
    });
  }
}
