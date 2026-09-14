using System;
using System.Buffers.Binary;
using FileFormat.Codecs;
using FileFormat.Core;

namespace FileFormat.Codecs.MagicYuv.Tests;

[TestFixture]
public sealed class MagicYuvDeepTests {
  [TestCase("M0G0", 10)]
  public void DeepGreyRoundTripsExactly(string code, int bits) {
    const int width = 9;
    const int height = 7;
    var picture = _Planar(width, height, PixelFormat.Gray10, bits, 0, 0, 11);
    var encoder = MagicYuvEncoder.Create(_Stream(code, width, height, 10), MagicYuvEncoder.Predictor.Median, 3);

    Assert.That(encoder.TryEncode(picture, 7, out var packet), Is.True);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(packet.PresentationTimestamp));
      Assert.That(packet.Data.Span[10], Is.EqualTo(bits + 4));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray10));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }

  [TestCase("M0Y4", PixelFormat.Yuv444P10, 0, 0, 30)]
  [TestCase("M0Y2", PixelFormat.Yuv422P10, 1, 0, 20)]
  [TestCase("M0Y0", PixelFormat.Yuv420P10, 1, 1, 15)]
  public void TenBitYuvRoundTripsNativePlanes(
    string code,
    PixelFormat pixelFormat,
    int horizontalShift,
    int verticalShift,
    int streamBitsPerPixel
  ) {
    const int width = 9;
    const int height = 7;
    var picture = _Planar(width, height, pixelFormat, 10, horizontalShift, verticalShift, 23);
    var encoder = MagicYuvEncoder.Create(
      _Stream(code, width, height, streamBitsPerPixel),
      MagicYuvEncoder.Predictor.Gradient,
      3);

    Assert.That(encoder.TryEncode(picture, 1, out var packet), Is.True);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(pixelFormat));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(streamBitsPerPixel));
    });
  }

  [TestCase("M0RG", false, 10, 30)]
  [TestCase("M0RA", true, 10, 40)]
  [TestCase("M2RG", false, 12, 36)]
  [TestCase("M2RA", true, 12, 48)]
  [TestCase("M4RG", false, 14, 42)]
  [TestCase("M4RA", true, 14, 56)]
  public void DeepRgbRoundTripsEveryNativeDepth(
    string code,
    bool alpha,
    int bits,
    int streamBitsPerPixel
  ) {
    const int width = 11;
    const int height = 6;
    var picture = _DeepRgb(width, height, bits, alpha);
    var encoder = MagicYuvEncoder.Create(
      _Stream(code, width, height, streamBitsPerPixel),
      MagicYuvEncoder.Predictor.Median,
      2);

    Assert.That(encoder.TryEncode(picture, 2, out var packet), Is.True);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.Data.Span[10], Is.EqualTo(bits + 4));
      Assert.That(decoded.Format, Is.EqualTo(alpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48));
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(streamBitsPerPixel));
    });
  }

  [TestCase(MagicYuvEncoder.Predictor.Left)]
  [TestCase(MagicYuvEncoder.Predictor.Gradient)]
  [TestCase(MagicYuvEncoder.Predictor.Median)]
  public void DeepPredictionRoundTripsAllThreeModes(MagicYuvEncoder.Predictor predictor) {
    const int width = 13;
    const int height = 9;
    var picture = _DeepRgb(width, height, 12, false);
    var encoder = MagicYuvEncoder.Create(_Stream("M2RG", width, height, 36), predictor, 3);

    encoder.TryEncode(picture, 0, out var packet);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(packet, out var decoded);

    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  public void InterlacedPredictionUsesThePreviousRowOfTheSameField() {
    const int width = 9;
    const int height = 8;
    var picture = _Planar(width, height, PixelFormat.Gray10, 10, 0, 0, 31);
    var encoder = MagicYuvEncoder.Create(
      _Stream("M0G0", width, height, 10),
      MagicYuvEncoder.Predictor.Median,
      2,
      interlaced: true);

    encoder.TryEncode(picture, 0, out var packet);
    var frame = packet.Data.Span;
    var flags = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..16]);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(packet, out var decoded);

    Assert.Multiple(() => {
      Assert.That(flags & 2, Is.EqualTo(2), "interlace flag");
      Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
    });
  }

  [Test]
  public void DeepRawSlicesArePackedAtTheirActualBitDepth() {
    var picture = _Planar(1, 1, PixelFormat.Gray10, 10, 0, 0, 5);
    var encoder = MagicYuvEncoder.Create(
      _Stream("M0G0", 1, 1, 10), MagicYuvEncoder.Predictor.Left, 1);

    encoder.TryEncode(picture, 0, out var packet);
    var frame = packet.Data.Span;
    var firstSlice = 32 + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[36..40]));
    var tablesEnd = 32 + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[32..36]));
    var descriptorStart = 32 + 8 + 1 + 1;

    Assert.Multiple(() => {
      Assert.That(frame[firstSlice], Is.EqualTo(1), "one-sample deep slice should be stored raw");
      Assert.That(tablesEnd - descriptorStart, Is.LessThan(1024), "deep code lengths should use RLE descriptors");
    });

    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(packet, out var decoded);
    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  public void Yuva444CanBeWrittenAndPreservesAlpha() {
    const int width = 7;
    const int height = 5;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i * 17);
      pixels[i * 4 + 1] = (byte)(255 - i * 7);
      pixels[i * 4 + 2] = (byte)(i * 29);
      pixels[i * 4 + 3] = (byte)(i * 13);
    }
    var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var encoder = MagicYuvEncoder.Create(_Stream("M8YA", width, height, 32));

    encoder.TryEncode(picture, 0, out var packet);
    var decoder = MagicYuvDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(packet, out var decoded);

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    for (var i = 0; i < width * height; ++i)
      Assert.That(decoded.PixelData[i * 4 + 3], Is.EqualTo(pixels[i * 4 + 3]), $"alpha sample {i}");
  }

  private static MediaStreamInfo _Stream(string code, int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static RawImage _Planar(
    int width,
    int height,
    PixelFormat format,
    int bits,
    int horizontalShift,
    int verticalShift,
    int seed
  ) {
    var mask = (1 << bits) - 1;
    var chromaWidth = (width + (1 << horizontalShift) - 1) >> horizontalShift;
    var chromaHeight = (height + (1 << verticalShift) - 1) >> verticalShift;
    var planes = format is PixelFormat.Gray10 ? 1 : 3;
    var samples = checked(width * height + (planes == 1 ? 0 : 2 * chromaWidth * chromaHeight));
    var data = new byte[samples * 2];
    var at = 0;
    for (var plane = 0; plane < planes; ++plane) {
      var planeWidth = plane == 0 ? width : chromaWidth;
      var planeHeight = plane == 0 ? height : chromaHeight;
      for (var y = 0; y < planeHeight; ++y)
        for (var x = 0; x < planeWidth; ++x) {
          var value = (ushort)((seed + plane * 211 + x * 37 + y * 73 + x * y * 3) & mask);
          BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), value);
          at += 2;
        }
    }

    return new RawImage { Width = width, Height = height, Format = format, PixelData = data };
  }

  private static RawImage _DeepRgb(int width, int height, int bits, bool alpha) {
    var mask = (1 << bits) - 1;
    var channels = alpha ? 4 : 3;
    var data = new byte[width * height * channels * 2];
    for (var i = 0; i < width * height; ++i)
      for (var channel = 0; channel < channels; ++channel) {
        var native = (i * 149 + channel * 307 + i * channel * 11 + 17) & mask;
        var expanded = (ushort)((native * 65535L + mask / 2) / mask);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((i * channels + channel) * 2, 2), expanded);
      }

    return new RawImage {
      Width = width,
      Height = height,
      Format = alpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48,
      PixelData = data,
    };
  }
}
