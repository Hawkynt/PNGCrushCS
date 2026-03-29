using System;
using FileFormat.Rembrandt;

namespace FileFormat.Rembrandt.Tests;

[TestFixture]
public sealed class RembrandtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RembrandtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectSize() {
    var file = new RembrandtFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 2],
    };

    var bytes = RembrandtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(RembrandtFile.HeaderSize + 320 * 240 * 2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsWrittenBE() {
    var file = new RembrandtFile {
      Width = 640,
      Height = 480,
      PixelData = new byte[640 * 480 * 2],
    };

    var bytes = RembrandtWriter.ToBytes(file);

    // 640 = 0x0280 => BE: 0x02, 0x80
    Assert.That(bytes[0], Is.EqualTo(0x02));
    Assert.That(bytes[1], Is.EqualTo(0x80));
    // 480 = 0x01E0 => BE: 0x01, 0xE0
    Assert.That(bytes[2], Is.EqualTo(0x01));
    Assert.That(bytes[3], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[320 * 240 * 2];
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    pixelData[pixelData.Length - 2] = 0x00;
    pixelData[pixelData.Length - 1] = 0x1F;

    var file = new RembrandtFile {
      Width = 320,
      Height = 240,
      PixelData = pixelData,
    };

    var bytes = RembrandtWriter.ToBytes(file);

    Assert.That(bytes[RembrandtFile.HeaderSize], Is.EqualTo(0xF8));
    Assert.That(bytes[RembrandtFile.HeaderSize + 1], Is.EqualTo(0x00));
    Assert.That(bytes[bytes.Length - 2], Is.EqualTo(0x00));
    Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0x1F));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var file = new RembrandtFile {
      Width = 10,
      Height = 10,
      PixelData = new byte[5], // shorter than expected
    };

    var bytes = RembrandtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(RembrandtFile.HeaderSize + 10 * 10 * 2));
    Assert.That(bytes[RembrandtFile.HeaderSize + 5], Is.EqualTo(0));
  }
}
