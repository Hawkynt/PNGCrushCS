using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public class AvuiVideoEncoderTests {

  private static readonly CodecTag _Avui = CodecTag.FromCharacters("AVUI");
  private const int _Width = 720;

  private static MediaStreamInfo _Stream(
    int height = 486,
    int depth = AvuiVideoFormat.OpaqueDepth,
    ReadOnlyMemory<byte> privateData = default) => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _Avui,
      Handler = _Avui,
      Width = _Width,
      Height = height,
      BitsPerPixel = depth,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      CodecPrivateData = privateData,
    };

  private static byte[] _Aprg(bool interlaced) {
    var result = new byte[24];
    result[3] = 24;
    "APRGAPRG0001"u8.CopyTo(result.AsSpan(4));
    result[19] = interlaced ? (byte)2 : (byte)1;
    return result;
  }

  private static RawImage _Yuv422(int height) {
    var lumaLength = _Width * height;
    var chromaLength = (_Width / 2) * height;
    var pixels = new byte[lumaLength + chromaLength * 2];
    var cb = lumaLength;
    var cr = lumaLength + chromaLength;

    for (var row = 0; row < height; ++row) {
      var y = row * _Width;
      var c = row * (_Width / 2);
      pixels[y] = (byte)(10 + row % 100);
      pixels[y + 1] = (byte)(11 + row % 100);
      pixels[cb + c] = (byte)(20 + row % 100);
      pixels[cr + c] = (byte)(30 + row % 100);
    }

    return new() {
      Width = _Width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }

  [Test]
  [Category("Unit")]
  public void RefusesNonD1Geometry() {
    var stream = _Stream();
    stream = new MediaStreamInfo {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = stream.Codec,
      Width = _Width,
      Height = 480,
      BitsPerPixel = stream.BitsPerPixel,
    };

    Assert.That(() => AvuiVideoEncoder.Create(stream), Throws.TypeOf<NotSupportedException>());
  }

  [TestCase(8)]
  [TestCase(24)]
  [TestCase(48)]
  [Category("Unit")]
  public void RefusesUndefinedSampleDepths(int depth) {
    Assert.That(() => AvuiVideoEncoder.Create(_Stream(depth: depth)), Throws.TypeOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void NewStreamsAreProgressiveAndMatchTheReferencePacketLength() {
    const int height = 486;
    const int skip = 10;
    var encoder = AvuiVideoEncoder.Create(_Stream(height));

    Assert.That(encoder.TryEncode(_Yuv422(height), 7, out var packet), Is.True);

    var firstRow = 2 * _Width * skip;
    Assert.Multiple(() => {
      Assert.That(packet.Data.Length, Is.EqualTo(2 * _Width * (height + skip)));
      Assert.That(packet.Data.Span[..firstRow].ToArray(), Is.All.Zero);
      Assert.That(packet.Data.Span.Slice(firstRow, 4).ToArray(), Is.EqualTo(new byte[] { 20, 10, 30, 11 }));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void ExistingInterlacedDescriptionIsPreservedIncludingNtscFieldOrder() {
    const int height = 486;
    const int halfBlank = _Width * 10;
    const int fieldPayload = _Width * height;
    var encoder = AvuiVideoEncoder.Create(_Stream(height, privateData: _Aprg(interlaced: true)));

    Assert.That(encoder.TryEncode(_Yuv422(height), null, out var packet), Is.True);

    var opaqueLength = 2 * _Width * (height + 10) + 4;
    var secondField = halfBlank + fieldPayload + 4 + halfBlank;
    Assert.Multiple(() => {
      Assert.That(packet.Data.Length, Is.EqualTo(opaqueLength + 4));
      Assert.That(packet.Data.Span.Slice(halfBlank, 4).ToArray(), Is.EqualTo(new byte[] { 21, 11, 31, 12 }),
        "NTSC odd row 1 is the first coded field");
      Assert.That(packet.Data.Span.Slice(secondField, 4).ToArray(), Is.EqualTo(new byte[] { 20, 10, 30, 11 }),
        "even row 0 is the second coded field");
      Assert.That(packet.Data.Span[^4..].ToArray(), Is.All.Zero, "reference packets retain four trailing zero bytes");
    });
  }

  [Test]
  [Category("Unit")]
  public void Depth32WritesTheInvertedAlphaCompanion() {
    const int height = 486;
    const int skip = 10;
    var pixels = new byte[_Width * height * 4];
    for (var i = 0; i < _Width * height; ++i) {
      pixels[i * 4] = 16;
      pixels[i * 4 + 1] = 32;
      pixels[i * 4 + 2] = 48;
      pixels[i * 4 + 3] = 255;
    }
    pixels[3] = 0;
    pixels[7] = 128;

    var frame = new RawImage {
      Width = _Width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };
    var encoder = AvuiVideoEncoder.Create(_Stream(height, AvuiVideoFormat.AlphaDepth));

    Assert.That(encoder.TryEncode(frame, null, out var packet), Is.True);

    var opaqueLength = 2 * _Width * (height + skip);
    var alpha = opaqueLength + 5 + 2 * _Width * skip;
    Assert.Multiple(() => {
      Assert.That(packet.Data.Length, Is.EqualTo(2 * opaqueLength + 4));
      Assert.That(packet.Data.Span[alpha], Is.EqualTo(255));
      Assert.That(packet.Data.Span[alpha + 1], Is.Zero);
      Assert.That(packet.Data.Span[alpha + 2], Is.EqualTo(127));
    });
  }

  [Test]
  [Category("Unit")]
  public void Depth16RefusesActualTransparencyInsteadOfDroppingIt() {
    const int height = 486;
    var pixels = new byte[_Width * height * 4];
    for (var i = 3; i < pixels.Length; i += 4)
      pixels[i] = 255;
    pixels[3] = 127;

    var frame = new RawImage {
      Width = _Width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };
    var encoder = AvuiVideoEncoder.Create(_Stream(height));

    var failure = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(frame, null, out _));
    Assert.That(failure!.Message, Does.Contain("BitsPerPixel = 32"));
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamCarriesACompleteQuickTimeSampleEntry() {
    var encoder = AvuiVideoEncoder.Create(_Stream(576, AvuiVideoFormat.AlphaDepth, _Aprg(interlaced: true)));
    var stream = encoder.DescribeStream();
    var entry = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(entry), Is.EqualTo(entry.Length));
      Assert.That(entry.AsSpan(4, 4).ToArray(), Is.EqualTo("AVUI"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(32)), Is.EqualTo(_Width));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(34)), Is.EqualTo(576));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(82)), Is.EqualTo(32));
      Assert.That(entry.AsSpan(90, 12).ToArray(), Is.EqualTo("APRGAPRG0001"u8.ToArray()));
      Assert.That(entry[105], Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(130)), Is.EqualTo(_Width));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(134)), Is.EqualTo(576));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterAndReaderPreserveEveryYuvSample() {
    const int height = 576;
    var source = _Yuv422(height);
    var encoder = AvuiVideoEncoder.Create(_Stream(height));
    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);

    var decoder = AvuiVideoDecoder.Create(encoder.DescribeStream());
    var (y, cb, cr) = decoder.DecodePlanes(packet.Data.Span);
    var lumaLength = _Width * height;
    var chromaLength = (_Width / 2) * height;

    Assert.Multiple(() => {
      Assert.That(y, Is.EqualTo(source.PixelData.AsSpan(0, lumaLength).ToArray()));
      Assert.That(cb, Is.EqualTo(source.PixelData.AsSpan(lumaLength, chromaLength).ToArray()));
      Assert.That(cr, Is.EqualTo(source.PixelData.AsSpan(lumaLength + chromaLength, chromaLength).ToArray()));
    });
  }
}
