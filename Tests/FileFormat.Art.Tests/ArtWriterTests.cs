using System;
using System.Buffers.Binary;
using FileFormat.Art;

namespace FileFormat.Art.Tests;

[TestFixture]
public sealed class ArtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ArtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Version1_WritesVersionOne() {
    var bytes = ArtWriter.ToBytes(new ArtFile { TileStart = 0, Tiles = [] });

    var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Header_WritesCorrectTileRange() {
    var tile = new ArtTile { Width = 2, Height = 2, PixelData = new byte[4] };
    var bytes = ArtWriter.ToBytes(new ArtFile { TileStart = 10, Tiles = [tile, tile] });

    var tileStart = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    var tileEnd = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));

    Assert.That(tileStart, Is.EqualTo(10));
    Assert.That(tileEnd, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TileDimensions_WrittenCorrectly() {
    var tile = new ArtTile { Width = 64, Height = 128, PixelData = new byte[64 * 128] };
    var bytes = ArtWriter.ToBytes(new ArtFile { TileStart = 0, Tiles = [tile] });

    var width = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(ArtHeader.StructSize));
    var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(ArtHeader.StructSize + 2));

    Assert.That(width, Is.EqualTo(64));
    Assert.That(height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataStoredColumnMajor() {
    // Row-major input: row0=[1,2], row1=[3,4] for a 2x2 tile
    var tile = new ArtTile { Width = 2, Height = 2, PixelData = [1, 2, 3, 4] };
    var bytes = ArtWriter.ToBytes(new ArtFile { TileStart = 0, Tiles = [tile] });

    // Pixel data starts after header(16) + widths(2) + heights(2) + picanm(4) = 24
    var pixelStart = ArtHeader.StructSize + 2 + 2 + 4;
    // Column-major: col0=[1,3], col1=[2,4]
    Assert.That(bytes[pixelStart], Is.EqualTo(1));     // (0,0)
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(3)); // (0,1)
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(2)); // (1,0)
    Assert.That(bytes[pixelStart + 3], Is.EqualTo(4)); // (1,1)
  }
}
