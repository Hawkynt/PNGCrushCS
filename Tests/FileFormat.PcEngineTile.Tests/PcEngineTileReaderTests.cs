using System;
using System.IO;
using FileFormat.PcEngineTile;

namespace FileFormat.PcEngineTile.Tests;

[TestFixture]
public sealed class PcEngineTileReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pce"));
    Assert.Throws<FileNotFoundException>(() => PcEngineTileReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[31];
    Assert.Throws<InvalidDataException>(() => PcEngineTileReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotMultipleOf32_ThrowsInvalidDataException() {
    var badSize = new byte[33];
    Assert.Throws<InvalidDataException>(() => PcEngineTileReader.FromBytes(badSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[32];
    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_PixelDataLength() {
    var data = new byte[32];
    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(128 * 8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_DecodesAllFourPlanes() {
    // Tile: all four plane bytes for row0 have MSB set
    // plane0 row0 = offset 0*2 = 0, plane1 row0 = offset 0*2+1 = 1
    // plane2 row0 = offset 16+0*2 = 16, plane3 row0 = offset 16+0*2+1 = 17
    // pixel 0 = b0|b1<<1|b2<<2|b3<<3 = 1|2|4|8 = 15
    var data = new byte[32];
    data[0] = 0x80;   // plane0 row0
    data[1] = 0x80;   // plane1 row0
    data[16] = 0x80;  // plane2 row0
    data[17] = 0x80;  // plane3 row0

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(15));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane0Only() {
    var data = new byte[32];
    data[0] = 0x80;  // plane0 row0, MSB

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane1Only() {
    var data = new byte[32];
    data[1] = 0x80;  // plane1 row0, MSB

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane2Only() {
    var data = new byte[32];
    data[16] = 0x80;  // plane2 row0, MSB

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane3Only() {
    var data = new byte[32];
    data[17] = 0x80;  // plane3 row0, MSB

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleTiles_ParsesDimensions() {
    // 32 tiles = 2 rows of 16 tiles
    var data = new byte[32 * 32];
    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PartialRow_CeilsToFullRow() {
    // 17 tiles = needs 2 rows of tiles (ceil(17/16) = 2)
    var data = new byte[17 * 32];
    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[32];
    data[0] = 0xFF;  // plane0 row0, all pixels bit0=1
    using var stream = new MemoryStream(data);

    var result = PcEngineTileReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Row1_DecodesCorrectly() {
    // Test row 1: plane0 at offset 2, plane1 at offset 3
    var data = new byte[32];
    data[2] = 0x80;  // plane0 row1, MSB
    data[3] = 0x80;  // plane1 row1, MSB

    var result = PcEngineTileReader.FromBytes(data);

    // pixel at (0, 1) in tile = pixel at (0, 1) in image
    Assert.That(result.PixelData[128 * 1 + 0], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_AllPixelValues() {
    // Encode pixel values 0-7 across row0 bits 0-7
    var data = new byte[32];
    // pixel 0 = 0 (no bits set)
    // pixel 1 = 1 (plane0 bit6)
    // pixel 2 = 2 (plane1 bit5)
    // pixel 3 = 3 (plane0+plane1 bit4)
    // pixel 4 = 4 (plane2 bit3)
    // pixel 5 = 5 (plane0+plane2 bit2)
    // pixel 6 = 6 (plane1+plane2 bit1)
    // pixel 7 = 7 (plane0+plane1+plane2 bit0)
    data[0] = 0b01010101;   // plane0 row0: bits 6,4,2,0
    data[1] = 0b00110011;   // plane1 row0: bits 5,4,1,0
    data[16] = 0b00001111;  // plane2 row0: bits 3,2,1,0

    var result = PcEngineTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(2));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
    Assert.That(result.PixelData[4], Is.EqualTo(4));
    Assert.That(result.PixelData[5], Is.EqualTo(5));
    Assert.That(result.PixelData[6], Is.EqualTo(6));
    Assert.That(result.PixelData[7], Is.EqualTo(7));
  }
}
