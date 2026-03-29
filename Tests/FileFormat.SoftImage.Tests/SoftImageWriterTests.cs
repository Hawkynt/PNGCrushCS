using System;
using System.Buffers.Binary;
using FileFormat.SoftImage;

namespace FileFormat.SoftImage.Tests;

[TestFixture]
public sealed class SoftImageWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMagicBytes() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(SoftImageFile.Magic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIsAtLeast96Bytes() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsAreBigEndian() {
    var file = new SoftImageFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(88));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(90));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CommentFieldWritten() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Comment = "Hello PIC",
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo((byte)'H'));
    Assert.That(bytes[9], Is.EqualTo((byte)'e'));
    Assert.That(bytes[10], Is.EqualTo((byte)'l'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionFieldWritten() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    var versionBits = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    var version = BitConverter.Int32BitsToSingle(versionBits);
    Assert.That(version, Is.EqualTo(3.71f).Within(0.01f));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_HasChannelInfoPacket() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    Assert.That(bytes[96], Is.EqualTo(0));
    Assert.That(bytes[97], Is.EqualTo(8));
    Assert.That(bytes[98], Is.EqualTo(2));
    Assert.That(bytes[99], Is.EqualTo(0x70));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgba_HasTwoChannelInfoPackets() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0, 128],
      HasAlpha = true,
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    Assert.That(bytes[96], Is.EqualTo(1));
    Assert.That(bytes[97], Is.EqualTo(8));
    Assert.That(bytes[98], Is.EqualTo(2));
    Assert.That(bytes[99], Is.EqualTo(0x80));

    Assert.That(bytes[100], Is.EqualTo(0));
    Assert.That(bytes[101], Is.EqualTo(8));
    Assert.That(bytes[102], Is.EqualTo(2));
    Assert.That(bytes[103], Is.EqualTo(0x70));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputContainsPixelData() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 0, 0],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(100));
  }
}
