using System;
using System.IO;
using FileFormat.SnesTile;

namespace FileFormat.SnesTile.Tests;

[TestFixture]
public sealed class SnesTileReaderTests {

  [Test]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SnesTileReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SnesTileReader.FromBytes(new byte[16]));

  [Test]
  public void FromBytes_NotMultipleOfTileSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SnesTileReader.FromBytes(new byte[33]));

  [Test]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SnesTileReader.FromFile(null!));

  [Test]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => SnesTileReader.FromFile(new FileInfo("nonexistent.sfc")));

  [Test]
  public void FromBytes_OneTile_ParsesDimensions() {
    var data = new byte[32];
    var result = SnesTileReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(128));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  [Test]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SnesTileReader.FromStream(null!));

  [Test]
  public void FromBytes_SixteenTiles_HeightIs8() {
    var data = new byte[32 * 16];
    var result = SnesTileReader.FromBytes(data);
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  public void FromBytes_ThirtyTwoTiles_HeightIs16() {
    var data = new byte[32 * 32];
    var result = SnesTileReader.FromBytes(data);
    Assert.That(result.Height, Is.EqualTo(16));
  }
}
