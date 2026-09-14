extern alias Images;
using System;
using System.Buffers.Binary;
using BitmapInfoHeader = Images::FileFormat.Bmp.BitmapInfoHeader;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class LocoVideoNearLosslessTests {

  [Test]
  [Category("Unit")]
  public void VersionTwoWriterSuppressesResidualsInsideTheConfiguredLossBound() {
    var encoder = LocoVideoEncoder.Create(_Stream(2, 2, mode: 5, lossy: 1));
    var source = new byte[] { 129, 129, 129, 129, 129, 129 };

    Assert.That(encoder.TryEncode(new() {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = source,
    }, null, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x8E, 0x88, 0x88 }));
      Assert.That(_Version(encoder.DescribeStream()), Is.EqualTo(2));
      Assert.That(_Lossy(encoder.DescribeStream()), Is.EqualTo(1));
    });

    var decoder = LocoVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 128, 128, 128, 128, 128, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void VersionTwoWriterKeepsEveryDecodedSampleWithinTheConfiguredLossBound() {
    const int width = 8;
    const int height = 4;
    const int lossy = 3;
    var source = _Pattern(width, height);
    var encoder = LocoVideoEncoder.Create(_Stream(width, height, mode: 5, lossy));

    Assert.That(encoder.TryEncode(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = source,
    }, 17, out var packet), Is.True);

    var decoder = LocoVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData.Length, Is.EqualTo(source.Length));
    for (var i = 0; i < source.Length; ++i)
      Assert.That(Math.Abs(source[i] - decoded.PixelData[i]), Is.LessThanOrEqualTo(lossy), $"sample {i}");
  }

  [Test]
  [Category("Unit")]
  public void WriterRefusesUnknownCodecVersionsRatherThanGuessingTheirSemantics() {
    var stream = _Stream(2, 2, mode: 5, lossy: 1);
    BinaryPrimitives.WriteInt32LittleEndian(stream.CodecPrivateData.Span[BitmapInfoHeader.StructSize..], 3);

    var exception = Assert.Throws<NotSupportedException>(() => LocoVideoEncoder.Create(stream));
    Assert.That(exception!.Message, Does.Contain("versions 1 and 2"));
  }

  private static byte[] _Pattern(int width, int height) {
    var yLength = width * height;
    var chromaWidth = width / 2;
    var chromaHeight = height / 2;
    var chromaLength = chromaWidth * chromaHeight;
    var data = new byte[yLength + 2 * chromaLength];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        data[y * width + x] = (byte)(48 + ((x * 19 + y * 23) % 144));

    for (var y = 0; y < chromaHeight; ++y)
      for (var x = 0; x < chromaWidth; ++x) {
        data[yLength + y * chromaWidth + x] = (byte)(80 + ((x * 13 + y * 7) % 64));
        data[yLength + chromaLength + y * chromaWidth + x] = (byte)(112 + ((x * 11 + y * 17) % 64));
      }

    return data;
  }

  private static MediaStreamInfo _Stream(int width, int height, int mode, int lossy) {
    var format = new byte[BitmapInfoHeader.StructSize + 12];
    var extra = format.AsSpan(BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(extra, 2);
    BinaryPrimitives.WriteInt32LittleEndian(extra[4..], mode);
    BinaryPrimitives.WriteInt32LittleEndian(extra[8..], lossy);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("LOCO"),
      Width = width,
      Height = height,
      BitsPerPixel = 12,
      CodecPrivateData = format,
    };
  }

  private static int _Version(MediaStreamInfo stream)
    => BinaryPrimitives.ReadInt32LittleEndian(stream.CodecPrivateData.Span[BitmapInfoHeader.StructSize..]);

  private static int _Lossy(MediaStreamInfo stream)
    => BinaryPrimitives.ReadInt32LittleEndian(stream.CodecPrivateData.Span[(BitmapInfoHeader.StructSize + 8)..]);
}
