using System;
using FileFormat.Lss16;

namespace FileFormat.Lss16.Tests;

[TestFixture]
public sealed class Lss16WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Lss16Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytesCorrect() {
    var file = new Lss16File {
      Width = 4,
      Height = 1,
      PixelData = new byte[4],
    };

    var bytes = Lss16Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3D));
    Assert.That(bytes[1], Is.EqualTo(0xF3));
    Assert.That(bytes[2], Is.EqualTo(0x13));
    Assert.That(bytes[3], Is.EqualTo(0x14));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsStoredLittleEndian() {
    var file = new Lss16File {
      Width = 640,
      Height = 480,
      PixelData = new byte[640 * 480],
    };

    var bytes = Lss16Writer.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x80));
    Assert.That(bytes[5], Is.EqualTo(0x02));
    Assert.That(bytes[6], Is.EqualTo(0xE0));
    Assert.That(bytes[7], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteAtOffset8() {
    var file = new Lss16File {
      Width = 4,
      Height = 1,
      Palette = new byte[48],
      PixelData = new byte[4],
    };
    file.Palette[0] = 63;
    file.Palette[1] = 32;
    file.Palette[2] = 0;
    file.Palette[45] = 10;
    file.Palette[46] = 20;
    file.Palette[47] = 30;

    var bytes = Lss16Writer.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo(63));
    Assert.That(bytes[9], Is.EqualTo(32));
    Assert.That(bytes[10], Is.EqualTo(0));
    Assert.That(bytes[53], Is.EqualTo(10));
    Assert.That(bytes[54], Is.EqualTo(20));
    Assert.That(bytes[55], Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputStartsWithHeader() {
    var file = new Lss16File {
      Width = 2,
      Height = 1,
      PixelData = new byte[2],
    };

    var bytes = Lss16Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(Lss16File.HeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SmallDimensions_WidthCorrect() {
    var file = new Lss16File {
      Width = 3,
      Height = 2,
      PixelData = new byte[6],
    };

    var bytes = Lss16Writer.ToBytes(file);

    var width = bytes[4] | (bytes[5] << 8);
    var height = bytes[6] | (bytes[7] << 8);
    Assert.That(width, Is.EqualTo(3));
    Assert.That(height, Is.EqualTo(2));
  }
}
