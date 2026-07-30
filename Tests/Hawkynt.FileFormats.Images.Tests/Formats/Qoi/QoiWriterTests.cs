using System;
using System.Buffers.Binary;
using FileFormat.Qoi;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class QoiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb_StartsWithQoifSignature() {
    var file = new QoiFile {
      Width = 2,
      Height = 2,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = QoiWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'q'));
    Assert.That(bytes[1], Is.EqualTo((byte)'o'));
    Assert.That(bytes[2], Is.EqualTo((byte)'i'));
    Assert.That(bytes[3], Is.EqualTo((byte)'f'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthAndHeight_BigEndian() {
    var file = new QoiFile {
      Width = 320,
      Height = 240,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = QoiWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ChannelsByte_MatchesInput() {
    var file = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[4]
    };

    var bytes = QoiWriter.ToBytes(file);

    Assert.That(bytes[12], Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorSpaceByte_MatchesInput() {
    var file = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Linear,
      PixelData = new byte[3]
    };

    var bytes = QoiWriter.ToBytes(file);

    Assert.That(bytes[13], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndMarker_Present() {
    var file = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[3]
    };

    var bytes = QoiWriter.ToBytes(file);

    // Last 8 bytes should be 7 x 0x00 + 0x01
    var endOffset = bytes.Length - 8;
    for (var i = 0; i < 7; ++i)
      Assert.That(bytes[endOffset + i], Is.EqualTo(0x00), $"End marker byte {i} should be 0x00");
    Assert.That(bytes[endOffset + 7], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_AtLeastHeaderPlusEndMarker() {
    var file = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[3]
    };

    var bytes = QoiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(QoiHeader.StructSize + 8));
  }
}
