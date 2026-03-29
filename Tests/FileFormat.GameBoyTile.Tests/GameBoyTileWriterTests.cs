using System;
using FileFormat.GameBoyTile;

namespace FileFormat.GameBoyTile.Tests;

[TestFixture]
public sealed class GameBoyTileWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsMultipleOf16() {
    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    Assert.That(bytes.Length % 16, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleTile_OutputIs16Bytes() {
    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    // 16 tiles per row * 1 row = 16 tiles * 16 bytes = 256 bytes
    Assert.That(bytes.Length, Is.EqualTo(16 * 16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodesPixelValues() {
    var pixelData = new byte[128 * 8];
    // Set first 4 pixels of first tile to colors 0,1,2,3
    pixelData[0] = 0;
    pixelData[1] = 1;
    pixelData[2] = 2;
    pixelData[3] = 3;

    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    // plane0 bits 7..4 should be: 0,1,0,1 => 0b01010000 = 0x50
    // plane1 bits 7..4 should be: 0,0,1,1 => 0b00110000 = 0x30
    Assert.That(bytes[0], Is.EqualTo(0x50));
    Assert.That(bytes[1], Is.EqualTo(0x30));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllColor3_AllBitsSet() {
    var pixelData = new byte[128 * 8];
    // Set first tile row 0 to all color 3
    for (var i = 0; i < 8; ++i)
      pixelData[i] = 3;

    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF)); // plane0 all set
    Assert.That(bytes[1], Is.EqualTo(0xFF)); // plane1 all set
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Color1Only_Plane0SetPlane1Clear() {
    var pixelData = new byte[128 * 8];
    // Set first tile row 0 to all color 1
    for (var i = 0; i < 8; ++i)
      pixelData[i] = 1;

    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF)); // plane0 all set
    Assert.That(bytes[1], Is.EqualTo(0x00)); // plane1 all clear
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Color2Only_Plane0ClearPlane1Set() {
    var pixelData = new byte[128 * 8];
    // Set first tile row 0 to all color 2
    for (var i = 0; i < 8; ++i)
      pixelData[i] = 2;

    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00)); // plane0 all clear
    Assert.That(bytes[1], Is.EqualTo(0xFF)); // plane1 all set
  }
}
