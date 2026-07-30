using System;
using System.IO;
using FileFormat.NesChr;

namespace FileFormat.NesChr.Tests;

[TestFixture]
public sealed class NesChrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NesChrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NesChrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chr"));
    Assert.Throws<FileNotFoundException>(() => NesChrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NesChrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[15];
    Assert.Throws<InvalidDataException>(() => NesChrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotMultipleOf16_ThrowsInvalidDataException() {
    var badSize = new byte[17];
    Assert.Throws<InvalidDataException>(() => NesChrReader.FromBytes(badSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_ParsesDimensions() {
    var data = new byte[16];
    var result = NesChrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_PixelDataLength() {
    var data = new byte[16];
    var result = NesChrReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(128 * 8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_DecodesPlanes() {
    // Tile: plane0 row0 = 0b10000000 (pixel 0 has lo=1)
    //        plane1 row0 = 0b10000000 (pixel 0 has hi=1)
    // => pixel 0 value = (1 << 1) | 1 = 3
    var data = new byte[16];
    data[0] = 0x80;  // plane0 row0
    data[8] = 0x80;  // plane1 row0

    var result = NesChrReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane0Only() {
    // Only plane0 bit set => value = 1
    var data = new byte[16];
    data[0] = 0x80;  // plane0 row0, MSB

    var result = NesChrReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleTile_Plane1Only() {
    // Only plane1 bit set => value = 2
    var data = new byte[16];
    data[8] = 0x80;  // plane1 row0, MSB

    var result = NesChrReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleTiles_ParsesDimensions() {
    // 32 tiles = 2 rows of 16 tiles
    var data = new byte[32 * 16];
    var result = NesChrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PartialRow_CeilsToFullRow() {
    // 17 tiles = needs 2 rows of tiles (ceil(17/16) = 2)
    var data = new byte[17 * 16];
    var result = NesChrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[16];
    data[0] = 0xFF;  // plane0 row0, all pixels lo=1
    using var stream = new MemoryStream(data);

    var result = NesChrReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(1));
  }
}
