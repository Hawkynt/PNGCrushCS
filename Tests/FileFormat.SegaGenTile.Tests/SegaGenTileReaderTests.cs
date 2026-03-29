using System;
using System.IO;
using FileFormat.SegaGenTile;

namespace FileFormat.SegaGenTile.Tests;

[TestFixture]
public sealed class SegaGenTileReaderTests {

  [Test]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SegaGenTileReader.FromBytes(new byte[16]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SegaGenTileReader.FromBytes(new byte[33]));

  [Test]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileReader.FromFile(null!));

  [Test]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => SegaGenTileReader.FromFile(new FileInfo("nonexistent.gen")));

  [Test]
  public void FromBytes_OneTile_ParsesDimensions() {
    var data = new byte[32];
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(128));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  [Test]
  public void FromBytes_NibblePacking_HighNibbleIsLeftPixel() {
    var data = new byte[32];
    data[0] = 0xA5;
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(10));
      Assert.That(result.PixelData[1], Is.EqualTo(5));
    });
  }

  [Test]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileReader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[32];
    data[0] = 0x37;
    using var ms = new MemoryStream(data);
    var result = SegaGenTileReader.FromStream(ms);
    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(3));
      Assert.That(result.PixelData[1], Is.EqualTo(7));
    });
  }

  [Test]
  public void FromBytes_MultipleTiles_CorrectHeight() {
    var data = new byte[32 * 32];
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(128));
      Assert.That(result.Height, Is.EqualTo(16));
    });
  }

  [Test]
  public void FromBytes_PartialLastRow_HeightRoundsUp() {
    var data = new byte[32 * 17];
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(128));
      Assert.That(result.Height, Is.EqualTo(16));
    });
  }

  [Test]
  public void FromBytes_SecondTile_PixelsAtCorrectPosition() {
    var data = new byte[32 * 2];
    data[32] = 0xCD;
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.PixelData[8], Is.EqualTo(0x0C));
      Assert.That(result.PixelData[9], Is.EqualTo(0x0D));
    });
  }

  [Test]
  public void FromBytes_SecondRowOfTile_CorrectPosition() {
    var data = new byte[32];
    data[4] = 0xEF;
    var result = SegaGenTileReader.FromBytes(data);
    var py = 1;
    var px = 0;
    Assert.Multiple(() => {
      Assert.That(result.PixelData[py * 128 + px], Is.EqualTo(0x0E));
      Assert.That(result.PixelData[py * 128 + px + 1], Is.EqualTo(0x0F));
    });
  }

  [Test]
  public void FromBytes_AllNibbleValues_Correct() {
    var data = new byte[32];
    data[0] = 0x01;
    data[1] = 0x23;
    data[2] = 0x45;
    data[3] = 0x67;
    var result = SegaGenTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0));
      Assert.That(result.PixelData[1], Is.EqualTo(1));
      Assert.That(result.PixelData[2], Is.EqualTo(2));
      Assert.That(result.PixelData[3], Is.EqualTo(3));
      Assert.That(result.PixelData[4], Is.EqualTo(4));
      Assert.That(result.PixelData[5], Is.EqualTo(5));
      Assert.That(result.PixelData[6], Is.EqualTo(6));
      Assert.That(result.PixelData[7], Is.EqualTo(7));
    });
  }
}
