using System;
using System.Buffers.Binary;
using FileFormat.AutodeskCel;

namespace FileFormat.AutodeskCel.Tests;

[TestFixture]
public sealed class AutodeskCelWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesMagic0x9119() {
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    Assert.That(magic, Is.EqualTo(0x9119));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_MagicBytesAre0x19_0x91() {
    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x19));
    Assert.That(bytes[1], Is.EqualTo(0x91));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCorrectWidth() {
    var file = new AutodeskCelFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCorrectHeight() {
    var file = new AutodeskCelFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCorrectOffsets() {
    var file = new AutodeskCelFile {
      Width = 4,
      Height = 4,
      XOffset = 15,
      YOffset = 25,
      PixelData = new byte[16],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var xOff = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
    var yOff = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(xOff, Is.EqualTo(15));
    Assert.That(yOff, Is.EqualTo(25));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesBitsPerPixel() {
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[4],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var bpp = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));

    Assert.That(bpp, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCompressionByte() {
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      Compression = 0,
      PixelData = new byte[4],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);

    Assert.That(bytes[12], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_PaddingBytesAreZero() {
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);

    Assert.That(bytes[13], Is.EqualTo(0));
    Assert.That(bytes[14], Is.EqualTo(0));
    Assert.That(bytes[15], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_OutputSizeIsHeaderPlusPixelsPlusPalette() {
    var file = new AutodeskCelFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[8],
    };

    var bytes = AutodeskCelWriter.ToBytes(file);

    var expected = AutodeskCelFile.HeaderSize + 8 + AutodeskCelFile.PaletteSize;
    Assert.That(bytes.Length, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_PaletteAtEndIs6Bit() {
    var palette = new byte[AutodeskCelFile.PaletteSize];
    palette[0] = 252; // 252 / 4 = 63
    palette[1] = 128; // 128 / 4 = 32
    palette[2] = 4;   // 4 / 4 = 1

    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = palette,
    };

    var bytes = AutodeskCelWriter.ToBytes(file);
    var paletteOffset = AutodeskCelFile.HeaderSize + 1;

    Assert.That(bytes[paletteOffset], Is.EqualTo(63));
    Assert.That(bytes[paletteOffset + 1], Is.EqualTo(32));
    Assert.That(bytes[paletteOffset + 2], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_PixelDataPreserved() {
    var pixelData = new byte[] { 0, 1, 255, 128 };
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = AutodeskCelWriter.ToBytes(file);

    Assert.That(bytes[AutodeskCelFile.HeaderSize], Is.EqualTo(0));
    Assert.That(bytes[AutodeskCelFile.HeaderSize + 1], Is.EqualTo(1));
    Assert.That(bytes[AutodeskCelFile.HeaderSize + 2], Is.EqualTo(255));
    Assert.That(bytes[AutodeskCelFile.HeaderSize + 3], Is.EqualTo(128));
  }
}
