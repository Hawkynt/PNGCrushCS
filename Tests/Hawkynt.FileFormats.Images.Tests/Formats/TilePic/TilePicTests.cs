using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.TilePic;

namespace FileFormat.TilePic.Tests;

/// <summary>
/// TilePic, the Berkeley Digital Library's pyramid of JPEG tiles.
/// </summary>
/// <remarks>
/// No sample of the format could be found, so the fixtures are built here from <em>tilepic(5)</em>,
/// which is the layout comment out of the format's own source. Built this way they are read by
/// XnView's converter, which takes the first tile — the top of the pyramid — where this takes the
/// bottom layer, which is the picture.
/// </remarks>
[TestFixture]
public sealed class TilePicTests {

  private static RawImage _Picture(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 4 + seed);
      pixels[at + 1] = (byte)(y * 4 + seed);
      pixels[at + 2] = (byte)(seed * 40);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Jpeg(int width, int height, int seed)
    => JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(width, height, seed)));

  private static byte[] _Build(
    int imageWidth, int imageHeight, int tileWidth, int tileHeight,
    IReadOnlyList<byte[]> tiles, int layers, int scale, byte[]? attributes = null) {

    attributes ??= [];
    var index = (tiles.Count + 1) * TilePicFile.IndexEntrySize;
    var start = TilePicFile.HeaderSize + index;

    var body = 0;
    foreach (var tile in tiles)
      body += tile.Length;

    var file = new byte[start + body + attributes.Length];
    TilePicFile.Signature.CopyTo(file);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), TilePicFile.HeaderSize);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), (uint)imageWidth);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), (uint)imageHeight);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), (uint)tileWidth);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)tileHeight);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(24), (uint)tiles.Count);
    BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(28), (ushort)layers);
    BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(30), (ushort)scale);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(32), (uint)attributes.Length);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(36), 0);

    var at = start;
    for (var i = 0; i < tiles.Count; ++i) {
      BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(TilePicFile.HeaderSize + i * TilePicFile.IndexEntrySize), (uint)at);
      tiles[i].CopyTo(file, at);
      at += tiles[i].Length;
    }

    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(TilePicFile.HeaderSize + tiles.Count * TilePicFile.IndexEntrySize), (uint)at);
    attributes.CopyTo(file, at);
    return file;
  }

  /// <summary>One layer of one tile, which is the smallest file the format has.</summary>
  private static byte[] _Single() => _Build(32, 32, 32, 32, [_Jpeg(32, 32, 1)], layers: 1, scale: 2);

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TilePicReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_OneLayerOfOneTile_IsThePictureTheHeaderStates() {
    var file = TilePicReader.FromBytes(_Single());

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(32));
      Assert.That(file.Height, Is.EqualTo(32));
      Assert.That(file.PixelData, Has.Length.EqualTo(32 * 32 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_TwoLayers_TakesTheBottomOneAndPlacesItsTilesAcrossThenDown() {
    // 64 by 64 in tiles of 32: one tile in the upper layer and four in the lower. Each of the four
    // is a different picture, so a reader that laid them out in the wrong order would show it.
    var tiles = new List<byte[]> { _Jpeg(32, 32, 0), _Jpeg(32, 32, 1), _Jpeg(32, 32, 2), _Jpeg(32, 32, 3), _Jpeg(32, 32, 4) };
    var file = TilePicReader.FromBytes(_Build(64, 64, 32, 32, tiles, layers: 2, scale: 2));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(64));
      Assert.That(file.Height, Is.EqualTo(64));

      // The blue channel is the tile's own number times forty, which says which tile a corner came
      // from. Tiles one to four of the bottom layer are seeds one to four.
      Assert.That(file.PixelData[(0 * 64 + 0) * 3 + 2], Is.EqualTo(40).Within(24), "top left");
      Assert.That(file.PixelData[(0 * 64 + 40) * 3 + 2], Is.EqualTo(80).Within(24), "top right");
      Assert.That(file.PixelData[(40 * 64 + 0) * 3 + 2], Is.EqualTo(120).Within(24), "bottom left");
      Assert.That(file.PixelData[(40 * 64 + 40) * 3 + 2], Is.EqualTo(160).Within(24), "bottom right");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotTilePic_IsRefused()
    => Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(new byte[TilePicFile.HeaderSize + 64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_LayersThatDoNotAccountForTheTileCount_IsRefused() {
    // Two layers of 32-pixel tiles over a 64 by 64 picture need five tiles. Saying four is a header
    // that does not describe its own file, and reading it as far as it goes is how a reader draws a
    // quarter of a picture and calls it one.
    var tiles = new List<byte[]> { _Jpeg(32, 32, 1), _Jpeg(32, 32, 2), _Jpeg(32, 32, 3), _Jpeg(32, 32, 4) };

    Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(_Build(64, 64, 32, 32, tiles, layers: 2, scale: 2)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeLengthThatDoesNotReachTheEndOfTheFile_IsRefused() {
    var data = _Single();
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnIndexThatGoesBackwards_IsRefused() {
    var tiles = new List<byte[]> { _Jpeg(32, 32, 1), _Jpeg(32, 32, 2), _Jpeg(32, 32, 3), _Jpeg(32, 32, 4), _Jpeg(32, 32, 5) };
    var data = _Build(64, 64, 32, 32, tiles, layers: 2, scale: 2);

    // Send the third tile behind the second.
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(TilePicFile.HeaderSize + 2 * TilePicFile.IndexEntrySize), TilePicFile.HeaderSize);

    Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileThatIsNotAJpeg_IsRefused() {
    var data = _Single();
    var first = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(TilePicFile.HeaderSize));
    data[first] = 0x00;

    Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStatedHeaderSizeThatIsNotTheFormats_IsRefused() {
    var data = _Single();
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 44);

    Assert.Throws<InvalidDataException>(() => TilePicReader.FromBytes(data));
  }
}
