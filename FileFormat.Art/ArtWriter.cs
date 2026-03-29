using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Art;

/// <summary>Assembles Build Engine ART tile archive bytes from an <see cref="ArtFile"/>.</summary>
public static class ArtWriter {

  public static byte[] ToBytes(ArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var tiles = file.Tiles;
    var numTiles = tiles.Count;
    var tileEnd = file.TileStart + numTiles - 1;

    // Calculate total pixel data size
    var totalPixels = 0;
    for (var i = 0; i < numTiles; ++i)
      totalPixels += tiles[i].Width * tiles[i].Height;

    var arraysSize = numTiles * 2 + numTiles * 2 + numTiles * 4;
    var totalSize = ArtHeader.StructSize + arraysSize + totalPixels;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Write header
    var header = new ArtHeader(1, numTiles, file.TileStart, tileEnd);
    header.WriteTo(span);

    // Write width array
    var widthsOffset = ArtHeader.StructSize;
    var heightsOffset = widthsOffset + numTiles * 2;
    var picanmOffset = heightsOffset + numTiles * 2;

    for (var i = 0; i < numTiles; ++i) {
      var tile = tiles[i];
      BinaryPrimitives.WriteInt16LittleEndian(span[(widthsOffset + i * 2)..], (short)tile.Width);
      BinaryPrimitives.WriteInt16LittleEndian(span[(heightsOffset + i * 2)..], (short)tile.Height);

      // Pack PicAnm
      var numFrames = tile.NumFrames & 0x3F;
      var animType = ((int)tile.AnimType & 0x0F) << 6;
      var xOffset = (tile.XOffset & 0x3F) << 10;
      var yOffset = (tile.YOffset & 0xFF) << 16;
      var animSpeed = (tile.AnimSpeed & 0xFF) << 24;
      var picanm = numFrames | animType | xOffset | yOffset | animSpeed;
      BinaryPrimitives.WriteInt32LittleEndian(span[(picanmOffset + i * 4)..], picanm);
    }

    // Write pixel data (convert row-major to column-major)
    var pixelOffset = picanmOffset + numTiles * 4;
    for (var i = 0; i < numTiles; ++i) {
      var tile = tiles[i];
      var w = tile.Width;
      var h = tile.Height;

      if (w * h > 0) {
        for (var x = 0; x < w; ++x)
          for (var y = 0; y < h; ++y)
            result[pixelOffset + x * h + y] = tile.PixelData[y * w + x];

        pixelOffset += w * h;
      }
    }

    return result;
  }
}
