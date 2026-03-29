using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Gbr;

namespace FileFormat.Gbr.Tests;

[TestFixture]
public sealed class GbrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => GbrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicAtOffset20() {
    var file = new GbrFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "test",
      PixelData = new byte[4]
    };

    var bytes = GbrWriter.ToBytes(file);

    Assert.That(bytes[20], Is.EqualTo((byte)'G'));
    Assert.That(bytes[21], Is.EqualTo((byte)'I'));
    Assert.That(bytes[22], Is.EqualTo((byte)'M'));
    Assert.That(bytes[23], Is.EqualTo((byte)'P'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionIs2() {
    var file = new GbrFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "",
      PixelData = new byte[1]
    };

    var bytes = GbrWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(2u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSizeIncludesNameAndNull() {
    var name = "MyBrush";
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var expectedHeaderSize = 28 + nameBytes.Length + 1;
    var file = new GbrFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = name,
      PixelData = new byte[1]
    };

    var bytes = GbrWriter.ToBytes(file);
    var headerSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));

    Assert.That(headerSize, Is.EqualTo((uint)expectedHeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsWrittenBigEndian() {
    var file = new GbrFile {
      Width = 320,
      Height = 240,
      BytesPerPixel = 4,
      Spacing = 15,
      Name = "X",
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = GbrWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
    var bpp = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
    var spacing = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24));

    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
    Assert.That(bpp, Is.EqualTo(4u));
    Assert.That(spacing, Is.EqualTo(15u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new GbrFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "A",
      PixelData = pixelData
    };

    var bytes = GbrWriter.ToBytes(file);
    var nameBytes = Encoding.UTF8.GetBytes("A");
    var headerSize = 28 + nameBytes.Length + 1;

    Assert.That(bytes[headerSize], Is.EqualTo(0xAA));
    Assert.That(bytes[headerSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[headerSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[headerSize + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect() {
    var name = "Brush";
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var headerSize = 28 + nameBytes.Length + 1;
    var w = 4;
    var h = 3;
    var bpp = 4;
    var file = new GbrFile {
      Width = w,
      Height = h,
      BytesPerPixel = bpp,
      Spacing = 10,
      Name = name,
      PixelData = new byte[w * h * bpp]
    };

    var bytes = GbrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(headerSize + w * h * bpp));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyName_StillHasNullTerminator() {
    var file = new GbrFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "",
      PixelData = new byte[1]
    };

    var bytes = GbrWriter.ToBytes(file);
    var headerSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));

    // empty name: header_size = 28 + 0 + 1 = 29
    Assert.That(headerSize, Is.EqualTo(29u));
    Assert.That(bytes[28], Is.EqualTo(0)); // null terminator
  }
}
