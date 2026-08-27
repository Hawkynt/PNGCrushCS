using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class M101VideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheM101FourccIsTaken()
    => Assert.That(M101VideoDecoder.Accepts(_Stream(2, 2, 8, 4, 3)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(2, 2, 8, 4, 3);
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName), Does.Contain("Matrox Uncompressed SD"));
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<M101VideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void EightBitYuyvNeutralStudioBlackDecodesToBlack() {
    var decoder = M101VideoDecoder.Create(_Stream(2, 1, 8, 4, 3));
    var packet = new byte[] { 16, 128, 16, 128 };

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[6]));
  }

  [Test]
  [Category("Unit")]
  public void EightBitYuyvWhiteDecodesToWhite() {
    var decoder = M101VideoDecoder.Create(_Stream(2, 1, 8, 4, 3));
    var packet = new byte[] { 235, 128, 235, 128 };

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 255, 255, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TenBitPackingUsesTheLowTwoBitSideband() {
    var decoder = M101VideoDecoder.Create(_Stream(2, 1, 10, 40, 3));
    var packet = new byte[40];

    // Two luma samples at studio black 64: high eight bits 16, low two bits zero.
    // U/V neutral 512: high eight bits 128, low two bits zero.
    packet[0] = 16;
    packet[1] = 128;
    packet[2] = 16;
    packet[3] = 128;
    packet[32] = 0;

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[6]));

    // Raise only Y0's low two bits from 0 to 3. The first pixel must become slightly brighter while
    // the second remains black, proving the sideband participates rather than being discarded.
    packet[32] = 3;
    Assert.That(decoder.TryDecode(new(0, packet), out frame), Is.True);
    Assert.That(frame.PixelData[0], Is.GreaterThan((byte)0));
    Assert.That(frame.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FieldOrderRemapsStoredRowsBeforeColourConversion() {
    // Four rows, two pixels each. Bottom-field-first flags=0 make output rows source 2,0,3,1.
    var decoder = M101VideoDecoder.Create(_Stream(2, 4, 8, 4, 0));
    var packet = new byte[] {
      16, 128, 16, 128,
      235, 128, 235, 128,
      16, 128, 16, 128,
      235, 128, 235, 128,
    };

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    var rowBytes = 6;
    Assert.That(frame.PixelData.AsSpan(0, rowBytes).ToArray(), Is.EqualTo(new byte[rowBytes]));
    Assert.That(frame.PixelData.AsSpan(rowBytes, rowBytes).ToArray(), Is.EqualTo(new byte[rowBytes]));
    Assert.That(frame.PixelData.AsSpan(rowBytes * 2, rowBytes).ToArray(), Is.EqualTo(new byte[] { 255, 255, 255, 255, 255, 255 }));
    Assert.That(frame.PixelData.AsSpan(rowBytes * 3, rowBytes).ToArray(), Is.EqualTo(new byte[] { 255, 255, 255, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TooSmallStrideRefuses()
    => Assert.Throws<InvalidDataException>(() => M101VideoDecoder.Create(_Stream(4, 1, 8, 4, 3)));

  [Test]
  [Category("Unit")]
  public void UnknownBitDepthRefuses()
    => Assert.Throws<NotSupportedException>(() => M101VideoDecoder.Create(_Stream(2, 1, 9, 4, 3)));

  private static MediaStreamInfo _Stream(int width, int height, byte bits, int stride, byte fieldFlags) {
    var format = new byte[40 + 24];
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);
    format[40 + 8] = bits;
    format[40 + 12] = fieldFlags;
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(40 + 20), checked((uint)stride));

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("M101"),
      Width = width,
      Height = height,
      CodecPrivateData = format,
    };
  }
}
