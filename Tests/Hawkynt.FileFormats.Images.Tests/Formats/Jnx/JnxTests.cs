using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Jnx;
using FileFormat.Jpeg;
using NUnit.Framework;

namespace FileFormat.Jnx.Tests;

/// <summary>Garmin JNX maps, read and written.</summary>
/// <remarks>
/// No real JNX was available to read here, so the layout was taken from
/// ImageMagick's own coder and what this writes is handed back to ImageMagick to
/// judge. That is a third-party verdict in the direction that can be checked:
/// ImageMagick opens a map written here, counts its tiles, and reports each
/// tile's size, and its decode of the map is identical to its decode of the JPEG
/// the map carries.
/// </remarks>
[TestFixture]
public sealed class JnxTests {

  private static JnxTile _Tile(int index) {
    var width = 20 + index * 3;
    var height = 12 + index * 2;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i * 13 + index * 61) % 256);

    return new JnxTile {
      JpegData = JpegWriter.LossyEncode(
        pixels, width, height, quality: 90, JpegMode.Baseline, JpegSubsampling.Chroma444,
        optimizeHuffman: true, isGrayscale: false),
      Width = width,
      Height = height,
      NorthEastX = 1000 + index,
      NorthEastY = 2000 + index,
      SouthWestX = -1000 - index,
      SouthWestY = -2000 - index,
    };
  }

  private static JnxFile _Map(int tiles) {
    var list = new List<JnxTile>();
    for (var i = 0; i < tiles; ++i)
      list.Add(_Tile(i));

    return new JnxFile { Version = 3, LevelScales = [7], Tiles = list };
  }

  [TestCase(1)]
  [TestCase(4)]
  public void EveryTileComesBackWithItsPictureAndItsGroundUntouched(int tileCount) {
    var map = _Map(tileCount);
    var again = JnxReader.FromBytes(JnxWriter.ToBytes(map));

    Assert.That(again.Tiles, Has.Count.EqualTo(tileCount));
    for (var i = 0; i < tileCount; ++i) {
      var wrote = map.Tiles[i];
      var read = again.Tiles[i];
      Assert.Multiple(() => {
        Assert.That(read.Width, Is.EqualTo(wrote.Width), $"tile {i} width");
        Assert.That(read.Height, Is.EqualTo(wrote.Height), $"tile {i} height");
        Assert.That(read.NorthEastX, Is.EqualTo(wrote.NorthEastX), $"tile {i} north-east x");
        Assert.That(read.NorthEastY, Is.EqualTo(wrote.NorthEastY), $"tile {i} north-east y");
        Assert.That(read.SouthWestX, Is.EqualTo(wrote.SouthWestX), $"tile {i} south-west x");
        Assert.That(read.SouthWestY, Is.EqualTo(wrote.SouthWestY), $"tile {i} south-west y");
        Assert.That(read.JpegData, Is.EqualTo(wrote.JpegData), $"tile {i} picture");
      });
    }
  }

  /// <summary>
  /// The tile's start-of-image marker is not in the file, and the length beside
  /// it counts the bytes that are.
  /// </summary>
  [Test]
  public void TheStoredTileOmitsTheMarkerTheFormatLeavesOut() {
    var map = _Map(1);
    var bytes = JnxWriter.ToBytes(map);

    // Header of twelve fields, then the one level descriptor, then the tile.
    const int tileDescriptor = 4 * 12 + 12;
    var storedLength = BitConverter.ToInt32(bytes, tileDescriptor + 20);
    var storedOffset = BitConverter.ToInt32(bytes, tileDescriptor + 24);

    Assert.Multiple(() => {
      Assert.That(storedLength, Is.EqualTo(map.Tiles[0].JpegData.Length - 2));
      Assert.That(bytes[storedOffset], Is.Not.EqualTo(0xFF).Or.Not.EqualTo(0xD8));
      Assert.That(storedOffset + storedLength, Is.EqualTo(bytes.Length));
    });
  }

  [Test]
  public void AFileStatingAVersionTheFormatDoesNotDefineIsRefused() {
    var bytes = JnxWriter.ToBytes(_Map(1));
    bytes[0] = 9;
    Assert.Throws<InvalidDataException>(() => JnxReader.FromBytes(bytes));
  }

  [Test]
  public void AFileStatingMoreLevelsThanAMapHasIsRefused() {
    var bytes = JnxWriter.ToBytes(_Map(1));
    BitConverter.GetBytes(9999).CopyTo(bytes, 24);
    Assert.Throws<InvalidDataException>(() => JnxReader.FromBytes(bytes));
  }

  [Test]
  public void ATileReachingPastTheEndOfTheFileIsRefused() {
    var bytes = JnxWriter.ToBytes(_Map(1));
    const int tileDescriptor = 4 * 12 + 12;
    BitConverter.GetBytes(bytes.Length * 4).CopyTo(bytes, tileDescriptor + 20);
    Assert.Throws<InvalidDataException>(() => JnxReader.FromBytes(bytes));
  }

  /// <summary>A tile may state no offset at all, which is how a level leaves a square blank.</summary>
  [Test]
  public void ATileWithNoPictureIsPassedOverRatherThanDrawn() {
    var map = _Map(2);
    var bytes = JnxWriter.ToBytes(map);
    const int tileDescriptor = 4 * 12 + 12;
    BitConverter.GetBytes(-1).CopyTo(bytes, tileDescriptor + 24);

    var again = JnxReader.FromBytes(bytes);
    Assert.That(again.Tiles, Has.Count.EqualTo(1));
    Assert.That(again.Tiles[0].Width, Is.EqualTo(map.Tiles[1].Width));
  }
}
