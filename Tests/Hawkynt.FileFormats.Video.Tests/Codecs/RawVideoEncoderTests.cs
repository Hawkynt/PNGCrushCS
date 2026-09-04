using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

/// <summary>
/// The uncompressed <c>BI_RGB</c> encoder, measured against the package's own decoder of the same
/// packets: every stream it describes has to be one <see cref="RawVideoDecoder"/> accepts, and every
/// picture has to come back from it sample for sample.
/// </summary>
[TestFixture]
public sealed class RawVideoEncoderTests {

  private const int _HEADER_SIZE = 40;

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel = 0, byte[]? format = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    CodecPrivateData = format ?? ReadOnlyMemory<byte>.Empty,
  };

  private static byte[] _Random(int seed, int length) {
    var bytes = new byte[length];
    new Random(seed).NextBytes(bytes);
    return bytes;
  }

  /// <summary>A <c>BITMAPINFOHEADER</c> as an AVI's <c>strf</c> carries it, palette entries behind it as BGRX.</summary>
  private static byte[] _Header(int width, int height, int bitsPerPixel, int compression = 0, byte[]? paletteRgb = null) {
    var entries = paletteRgb == null ? 0 : paletteRgb.Length / 3;
    var format = new byte[_HEADER_SIZE + entries * 4];
    var span = format.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, _HEADER_SIZE);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], (short)bitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], compression);
    BinaryPrimitives.WriteInt32LittleEndian(span[32..], entries);
    for (var i = 0; i < entries; ++i) {
      format[_HEADER_SIZE + i * 4] = paletteRgb![i * 3 + 2];
      format[_HEADER_SIZE + i * 4 + 1] = paletteRgb[i * 3 + 1];
      format[_HEADER_SIZE + i * 4 + 2] = paletteRgb[i * 3];
    }

    return format;
  }

  private static RawImage _Decode(MediaStreamInfo described, CodedPacket packet) {
    Assert.That(RawVideoDecoder.Accepts(described), Is.True);
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder, Is.TypeOf<RawVideoDecoder>());
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    return frame;
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesABiRgbStreamTheDecoderAccepts() {
    var encoder = RawVideoEncoder.Create(_Stream(7, 5));
    var described = encoder.DescribeStream();
    var format = described.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.None));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("DIB ")));
      Assert.That(described.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(described.Width, Is.EqualTo(7));
      Assert.That(described.Height, Is.EqualTo(5));
      Assert.That(described.BitsPerPixel, Is.EqualTo(24));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.Length, Is.EqualTo(_HEADER_SIZE));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(_HEADER_SIZE));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(7));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(5));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(12)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(16)), Is.Zero);
      // biSizeImage: a 7-pixel row of 21 bytes padded to 24, five rows.
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(20)), Is.EqualTo(24 * 5));
      Assert.That(RawVideoDecoder.Accepts(described), Is.True);
      Assert.That(RawVideoEncoder.Codec, Is.EqualTo(CodecTag.None));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitStreamWithNoHeaderCarriesTheGreyRamp() {
    var described = RawVideoEncoder.Create(_Stream(4, 2, 8)).DescribeStream();
    var format = described.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(described.BitsPerPixel, Is.EqualTo(8));
      Assert.That(format.Length, Is.EqualTo(_HEADER_SIZE + 256 * 4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(32)), Is.EqualTo(256));
      Assert.That(format[_HEADER_SIZE + 200 * 4], Is.EqualTo(200));
      Assert.That(format[_HEADER_SIZE + 200 * 4 + 1], Is.EqualTo(200));
      Assert.That(format[_HEADER_SIZE + 200 * 4 + 2], Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatCarriesAHeaderKeepsItVerbatim() {
    var header = _Header(6, -3, 32);
    var described = RawVideoEncoder.Create(_Stream(6, 3, format: header)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.CodecPrivateData.ToArray(), Is.EqualTo(header));
      Assert.That(described.BitsPerPixel, Is.EqualTo(32));
      Assert.That(described.Height, Is.EqualTo(3));
    });
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

  [TestCase(3, 2, 1)]
  [TestCase(7, 5, 2)]
  [TestCase(16, 9, 3)]
  [TestCase(1, 1, 4)]
  [Category("Unit")]
  public void TwentyFourBitPicturesComeBackIdentical(int width, int height, int seed) {
    var pixels = _Random(seed, width * height * 3);
    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    var stride = (width * 3 + 3) & ~3;
    Assert.That(packet.Data.Length, Is.EqualTo(stride * height));

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      Assert.That(decoded.PixelData.AsSpan(0, pixels.Length).ToArray(), Is.EqualTo(pixels));
    });
  }

  [TestCase(3, 2, 11)]
  [TestCase(7, 5, 12)]
  [TestCase(16, 9, 13)]
  [Category("Unit")]
  public void ThirtyTwoBitPicturesComeBackIdentical(int width, int height, int seed) {
    var pixels = _Random(seed, width * height * 4);
    // The bitmap reader takes a fourth byte that is zero throughout as padding; one opaque pixel
    // makes it an alpha channel, which is what these pictures are meant to test.
    pixels[3] = 255;
    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(width, height, 32));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(width * 4 * height));

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgra32));
      Assert.That(decoded.PixelData.AsSpan(0, pixels.Length).ToArray(), Is.EqualTo(pixels));
    });
  }

  [TestCase(3, 2, 21)]
  [TestCase(7, 5, 22)]
  [TestCase(16, 9, 23)]
  [Category("Unit")]
  public void EightBitGreyPicturesComeBackIdentical(int width, int height, int seed) {
    var pixels = _Random(seed, width * height);
    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(width, height, 8));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(((width + 3) & ~3) * height));

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(decoded.PixelData.AsSpan(0, pixels.Length).ToArray(), Is.EqualTo(pixels));
    });
  }

  [TestCase(3, 2, 31)]
  [TestCase(7, 5, 32)]
  [TestCase(16, 9, 33)]
  [Category("Unit")]
  public void EightBitPalettisedPicturesComeBackWithTheirIndicesAndPalette(int width, int height, int seed) {
    var palette = _Random(seed + 100, 16 * 3);
    palette[0] = 10;
    palette[1] = 20;
    palette[2] = 30; // at least one entry that is not grey, so the reader keeps the palette
    var indices = _Random(seed, width * height).Select(static i => (byte)(i & 15)).ToArray();
    var frame = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = 16,
    };
    var encoder = RawVideoEncoder.Create(_Stream(width, height, format: _Header(width, height, 8, paletteRgb: palette)));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);

    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PixelData.AsSpan(0, indices.Length).ToArray(), Is.EqualTo(indices));
      Assert.That(decoded.PaletteCount, Is.EqualTo(16));
      Assert.That(decoded.Palette!.AsSpan(0, 48).ToArray(), Is.EqualTo(palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void ATopDownHeaderCodesRowsTopDown() {
    var pixels = _Random(41, 5 * 3 * 3);
    var frame = new RawImage { Width = 5, Height = 3, Format = PixelFormat.Bgr24, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(5, 3, format: _Header(5, -3, 24)));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.Data.Span[..15].ToArray(), Is.EqualTo(pixels[..15]));
      Assert.That(packet.Data.Span[16..31].ToArray(), Is.EqualTo(pixels[15..30]));
      Assert.That(_Decode(encoder.DescribeStream(), packet).PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void ABottomUpPacketHoldsTheLastRowFirstAndPadsEachRowToFourBytes() {
    var pixels = _Random(42, 3 * 3 * 2);
    var frame = new RawImage { Width = 3, Height = 2, Format = PixelFormat.Bgr24, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(3, 2));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var data = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(24));
      Assert.That(data[..9], Is.EqualTo(pixels[9..18]));
      Assert.That(data[9..12], Is.All.Zero);
      Assert.That(data[12..21], Is.EqualTo(pixels[..9]));
      Assert.That(data[21..24], Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherFormatIsConvertedToTheStreams() {
    var rgb = _Random(43, 4 * 3 * 3);
    var frame = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = rgb };
    var encoder = RawVideoEncoder.Create(_Stream(4, 3));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var decoded = _Decode(encoder.DescribeStream(), packet);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(rgb));
    });
  }

  [Test]
  [Category("Unit")]
  public void PacketsMuxIntoAnAviTheReaderHandsBackToTheSameDecoder() {
    var pixels = _Random(44, 7 * 5 * 3);
    var frame = new RawImage { Width = 7, Height = 5, Format = PixelFormat.Bgr24, PixelData = pixels };
    var encoder = RawVideoEncoder.Create(_Stream(7, 5));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder, Is.TypeOf<RawVideoDecoder>());
    Assert.That(decoder.TryDecode(AviContainer.ReadPackets(container).Single(), out var decoded), Is.True);
    Assert.That(decoded.PixelData.AsSpan(0, pixels.Length).ToArray(), Is.EqualTo(pixels));
  }

  // ============================================================================================
  // Packet bookkeeping
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameCarryingItsTimestamp() {
    var frame = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Bgr24, PixelData = new byte[12] };
    var encoder = RawVideoEncoder.Create(_Stream(2, 2));

    Assert.That(encoder.TryEncode(frame, 37, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(37));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(37));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });

    Assert.That(encoder.TryEncode(frame, null, out packet), Is.True);
    Assert.That(packet.PresentationTimestamp, Is.Null);
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = RawVideoEncoder.Create(_Stream(4, 4));
    var wrong = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Bgr24, PixelData = new byte[36] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
    Assert.That(failure!.Message, Does.Contain("4x3"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADepthItDoesNotWrite() {
    var failure = Assert.Throws<NotSupportedException>(() => RawVideoEncoder.Create(_Stream(4, 4, 16)));
    Assert.That(failure!.Message, Does.Contain("16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAHeaderStatingAnyCompression() {
    var failure = Assert.Throws<NotSupportedException>(() => RawVideoEncoder.Create(_Stream(4, 4, format: _Header(4, 4, 24, compression: 3))));
    Assert.That(failure!.Message, Does.Contain("BI_RGB"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAHeaderThatDisagreesWithTheStreamsGeometry() {
    Assert.Throws<InvalidDataException>(() => RawVideoEncoder.Create(_Stream(4, 4, format: _Header(8, 4, 24))));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAHeaderMissingThePaletteItClaims() {
    var header = _Header(4, 4, 8, paletteRgb: new byte[3 * 4]);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), 16);

    Assert.Throws<InvalidDataException>(() => RawVideoEncoder.Create(_Stream(4, 4, format: header)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesToQuantiseColourOntoAPalette() {
    var palette = new byte[16 * 3];
    palette[0] = 255;
    var encoder = RawVideoEncoder.Create(_Stream(4, 2, format: _Header(4, 2, 8, paletteRgb: palette)));
    var colour = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[24] };
    var otherPalette = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[8],
      Palette = new byte[16 * 3],
      PaletteCount = 16,
    };

    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(colour, 0, out _));
    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(otherPalette, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixelsAndAStreamThatIsNotVideo() {
    Assert.Throws<InvalidDataException>(() => RawVideoEncoder.Create(_Stream(0, 4)));
    Assert.Throws<NotSupportedException>(() => RawVideoEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 4 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfItsOwnPixelData() {
    var encoder = RawVideoEncoder.Create(_Stream(4, 4));
    var frame = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Bgr24, PixelData = new byte[10] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }
}
