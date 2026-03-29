using System;
using System.IO;
using FileFormat.MasterSystemTile;

namespace FileFormat.MasterSystemTile.Tests;

[TestFixture]
public sealed class MasterSystemTileReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MasterSystemTileReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MasterSystemTileReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sms"));
    Assert.Throws<FileNotFoundException>(() => MasterSystemTileReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MasterSystemTileReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[31];
    Assert.Throws<InvalidDataException>(() => MasterSystemTileReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotMultipleOf32_ThrowsInvalidDataException() {
    var badSize = new byte[33];
    Assert.Throws<InvalidDataException>(() => MasterSystemTileReader.FromBytes(badSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[32];
    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_PixelDataLength() {
    var data = new byte[32];
    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(128 * 8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_AllPlanesSet_ReturnsValue15() {
    // Row 0: plane0=0x80, plane1=0x80, plane2=0x80, plane3=0x80
    // => pixel 0 = 1|2|4|8 = 15
    var data = new byte[32];
    data[0] = 0x80; // plane0 row0
    data[1] = 0x80; // plane1 row0
    data[2] = 0x80; // plane2 row0
    data[3] = 0x80; // plane3 row0

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(15));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane0Only_ReturnsValue1() {
    var data = new byte[32];
    data[0] = 0x80; // plane0 row0, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane1Only_ReturnsValue2() {
    var data = new byte[32];
    data[1] = 0x80; // plane1 row0, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane2Only_ReturnsValue4() {
    var data = new byte[32];
    data[2] = 0x80; // plane2 row0, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane3Only_ReturnsValue8() {
    var data = new byte[32];
    data[3] = 0x80; // plane3 row0, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Planes01_ReturnsValue3() {
    var data = new byte[32];
    data[0] = 0x80; // plane0
    data[1] = 0x80; // plane1

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Planes23_ReturnsValue12() {
    var data = new byte[32];
    data[2] = 0x80; // plane2
    data[3] = 0x80; // plane3

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleTiles_ParsesDimensions() {
    // 32 tiles = 2 rows of 16 tiles
    var data = new byte[32 * 32];
    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PartialRow_CeilsToFullRow() {
    // 17 tiles = needs 2 rows of tiles (ceil(17/16) = 2)
    var data = new byte[17 * 32];
    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Row1_PlanesAtCorrectOffset() {
    // Row 1 starts at offset 4 (row * 4 planes)
    var data = new byte[32];
    data[4] = 0x80; // plane0 row1, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    // Pixel at (0, 1) in tile 0 = pixel at (0, 1) in image
    Assert.That(result.PixelData[128], Is.EqualTo(1)); // row 1, col 0
    Assert.That(result.PixelData[0], Is.EqualTo(0));   // row 0, col 0
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LastRow_PlanesAtCorrectOffset() {
    // Row 7 starts at offset 28 (7 * 4)
    var data = new byte[32];
    data[28] = 0x80; // plane0 row7, MSB

    var result = MasterSystemTileReader.FromBytes(data);

    // Pixel at (0, 7) in tile 0
    Assert.That(result.PixelData[7 * 128], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[32];
    data[0] = 0xFF; // plane0 row0, all bits set => all 8 pixels have bit 0 set
    using var stream = new MemoryStream(data);

    var result = MasterSystemTileReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
    for (var i = 0; i < 8; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo(1), $"Pixel {i} should be 1");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LSBPixel_IsLastInRow() {
    // LSB of plane byte = pixel 7 (rightmost in the 8-pixel group)
    var data = new byte[32];
    data[0] = 0x01; // plane0 row0, LSB = pixel at bit position 7

    var result = MasterSystemTileReader.FromBytes(data);

    Assert.That(result.PixelData[7], Is.EqualTo(1)); // pixel 7 in row 0
    Assert.That(result.PixelData[0], Is.EqualTo(0)); // pixel 0 should be 0
  }
}
