using System;
using System.Buffers.Binary;
using FileFormat.Pat;

namespace FileFormat.Pat.Tests;

[TestFixture]
public sealed class PatWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicAtOffset20() {
    var file = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = "t",
      PixelData = [0xFF]
    };

    var bytes = PatWriter.ToBytes(file);

    Assert.That((char)bytes[20], Is.EqualTo('G'));
    Assert.That((char)bytes[21], Is.EqualTo('P'));
    Assert.That((char)bytes[22], Is.EqualTo('A'));
    Assert.That((char)bytes[23], Is.EqualTo('T'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSizeField() {
    var file = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = "Hello",
      PixelData = [0x00]
    };

    var bytes = PatWriter.ToBytes(file);
    var headerSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));

    // 24 fixed + 5 name bytes + 1 null terminator = 30
    Assert.That(headerSize, Is.EqualTo(30u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionIs1() {
    var file = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = "v",
      PixelData = [0x00]
    };

    var bytes = PatWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(1u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsAndBpp() {
    var file = new PatFile {
      Width = 320,
      Height = 240,
      BytesPerPixel = 3,
      Name = "d",
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = PatWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
    var bpp = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));

    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
    Assert.That(bpp, Is.EqualTo(3u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataFollowsHeader() {
    var file = new PatFile {
      Width = 2,
      Height = 1,
      BytesPerPixel = 3,
      Name = "px",
      PixelData = [0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33]
    };

    var bytes = PatWriter.ToBytes(file);
    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));

    Assert.That(bytes[headerSize], Is.EqualTo(0xAA));
    Assert.That(bytes[headerSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[headerSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[headerSize + 3], Is.EqualTo(0x11));
    Assert.That(bytes[headerSize + 4], Is.EqualTo(0x22));
    Assert.That(bytes[headerSize + 5], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize() {
    var name = "test";
    var w = 4;
    var h = 3;
    var bpp = 4;
    var file = new PatFile {
      Width = w,
      Height = h,
      BytesPerPixel = bpp,
      Name = name,
      PixelData = new byte[w * h * bpp]
    };

    var bytes = PatWriter.ToBytes(file);
    var expectedHeaderSize = 24 + name.Length + 1;
    var expectedTotal = expectedHeaderSize + w * h * bpp;

    Assert.That(bytes.Length, Is.EqualTo(expectedTotal));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PatWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NameNullTerminated() {
    var file = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = "AB",
      PixelData = [0x00]
    };

    var bytes = PatWriter.ToBytes(file);

    // Name starts at offset 24: 'A'=65, 'B'=66, then null=0
    Assert.That(bytes[24], Is.EqualTo((byte)'A'));
    Assert.That(bytes[25], Is.EqualTo((byte)'B'));
    Assert.That(bytes[26], Is.EqualTo(0));
  }
}
