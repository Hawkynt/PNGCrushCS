using System;
using System.IO;
using FileFormat.GameBoyTile;

namespace FileFormat.GameBoyTile.Tests;

[TestFixture]
public sealed class GameBoyTileReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".2bpp"));
    Assert.Throws<FileNotFoundException>(() => GameBoyTileReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => GameBoyTileReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotMultipleOf16_ThrowsInvalidDataException() {
    var bad = new byte[20];
    Assert.Throws<InvalidDataException>(() => GameBoyTileReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTile_ParsesDimensions() {
    var data = new byte[16];
    var result = GameBoyTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTile_AllZeros_AllPixelsZero() {
    var data = new byte[16];
    var result = GameBoyTileReader.FromBytes(data);

    for (var i = 0; i < result.PixelData.Length; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo(0), $"Pixel at index {i} should be 0");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTile_DecodesPixelValues() {
    var data = new byte[16];
    // Row 0: plane0 = 0b10000000, plane1 = 0b10000000
    // First pixel: plane1 bit7=1, plane0 bit7=1 => color 3
    // Remaining: 0
    data[0] = 0x80; // plane0
    data[1] = 0x80; // plane1

    var result = GameBoyTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTile_DecodesAllFourColors() {
    var data = new byte[16];
    // Row 0: set up 4 pixels with colors 0,1,2,3
    // pixel 0: p0=0, p1=0 => color 0
    // pixel 1: p0=1, p1=0 => color 1
    // pixel 2: p0=0, p1=1 => color 2
    // pixel 3: p0=1, p1=1 => color 3
    // plane0 bits 7..4: 0,1,0,1 => 0b01010000 = 0x50
    // plane1 bits 7..4: 0,0,1,1 => 0b00110000 = 0x30
    data[0] = 0x50;
    data[1] = 0x30;

    var result = GameBoyTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(2));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleTiles_CorrectHeight() {
    // 32 tiles => 2 rows of 16 tiles => height = 16
    var data = new byte[32 * 16];
    var result = GameBoyTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PartialRow_RoundsUpHeight() {
    // 17 tiles => 2 rows (ceil(17/16) = 2) => height = 16
    var data = new byte[17 * 16];
    var result = GameBoyTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[16];
    using var stream = new MemoryStream(data);
    var result = GameBoyTileReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }
}
