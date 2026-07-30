using System;
using System.IO;
using FileFormat.Art;

namespace FileFormat.Art.Tests;

[TestFixture]
public sealed class ArtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".art"));
    Assert.Throws<FileNotFoundException>(() => ArtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[12];
    Assert.Throws<InvalidDataException>(() => ArtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_ThrowsInvalidDataException() {
    var bad = new byte[ArtHeader.StructSize];
    bad[0] = 99; // Version != 1
    Assert.Throws<InvalidDataException>(() => ArtReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleTile_ParsesCorrectly() {
    var tile = new ArtTile {
      Width = 4,
      Height = 3,
      AnimType = ArtAnimType.None,
      NumFrames = 0,
      XOffset = 0,
      YOffset = 0,
      AnimSpeed = 0,
      PixelData = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]
    };
    var art = ArtWriter.ToBytes(new ArtFile { TileStart = 0, Tiles = [tile] });

    var result = ArtReader.FromBytes(art);

    Assert.That(result.TileStart, Is.EqualTo(0));
    Assert.That(result.Tiles, Has.Count.EqualTo(1));
    Assert.That(result.Tiles[0].Width, Is.EqualTo(4));
    Assert.That(result.Tiles[0].Height, Is.EqualTo(3));
    Assert.That(result.Tiles[0].PixelData, Is.EqualTo(tile.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtReader.FromStream(null!));
  }
}
