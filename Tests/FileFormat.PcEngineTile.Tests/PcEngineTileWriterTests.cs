using System;
using FileFormat.PcEngineTile;

namespace FileFormat.PcEngineTile.Tests;

[TestFixture]
public sealed class PcEngineTileWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleTileRow_ReturnsTilesPerRowTimesBytesPerTile() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16 * 32)); // 16 tiles across x 32 bytes each
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsMultipleOf32() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 16,
      PixelData = new byte[128 * 16]
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes.Length % 32, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue15_AllFourPlanesSet() {
    var pixels = new byte[128 * 8];
    pixels[0] = 15; // pixel (0,0) = 15 => all four planes set

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x80));   // plane0 row0 MSB
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x80));   // plane1 row0 MSB
    Assert.That(bytes[16] & 0x80, Is.EqualTo(0x80));  // plane2 row0 MSB
    Assert.That(bytes[17] & 0x80, Is.EqualTo(0x80));  // plane3 row0 MSB
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue1_OnlyPlane0Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 1;

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x80));   // plane0 set
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x00));   // plane1 clear
    Assert.That(bytes[16] & 0x80, Is.EqualTo(0x00));  // plane2 clear
    Assert.That(bytes[17] & 0x80, Is.EqualTo(0x00));  // plane3 clear
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue2_OnlyPlane1Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 2;

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x00));   // plane0 clear
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x80));   // plane1 set
    Assert.That(bytes[16] & 0x80, Is.EqualTo(0x00));  // plane2 clear
    Assert.That(bytes[17] & 0x80, Is.EqualTo(0x00));  // plane3 clear
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue4_OnlyPlane2Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 4;

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x00));   // plane0 clear
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x00));   // plane1 clear
    Assert.That(bytes[16] & 0x80, Is.EqualTo(0x80));  // plane2 set
    Assert.That(bytes[17] & 0x80, Is.EqualTo(0x00));  // plane3 clear
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValue8_OnlyPlane3Set() {
    var pixels = new byte[128 * 8];
    pixels[0] = 8;

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x00));   // plane0 clear
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x00));   // plane1 clear
    Assert.That(bytes[16] & 0x80, Is.EqualTo(0x00));  // plane2 clear
    Assert.That(bytes[17] & 0x80, Is.EqualTo(0x80));  // plane3 set
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeros_AllBytesZero() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    for (var i = 0; i < bytes.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Byte at index {i} should be 0");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Row1_EncodesAtCorrectOffset() {
    var pixels = new byte[128 * 8];
    // pixel at (0, 1) = 5 => plane0 and plane2 set
    pixels[128 * 1 + 0] = 5;

    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(file);

    // row 1: plane0 at offset 1*2=2, plane2 at offset 16+1*2=18
    Assert.That(bytes[2] & 0x80, Is.EqualTo(0x80));   // plane0 row1 MSB
    Assert.That(bytes[3] & 0x80, Is.EqualTo(0x00));   // plane1 row1 MSB clear
    Assert.That(bytes[18] & 0x80, Is.EqualTo(0x80));  // plane2 row1 MSB
    Assert.That(bytes[19] & 0x80, Is.EqualTo(0x00));  // plane3 row1 MSB clear
  }
}
