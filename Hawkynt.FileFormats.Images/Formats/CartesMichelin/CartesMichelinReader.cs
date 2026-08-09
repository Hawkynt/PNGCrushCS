using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.CartesMichelin;

/// <summary>Reads Cartes Michelin sheets from bytes, streams, or file paths.</summary>
public static class CartesMichelinReader {

  public static CartesMichelinFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cartes Michelin file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CartesMichelinFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static CartesMichelinFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CartesMichelinFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CartesMichelinFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a Cartes Michelin sheet (at least {CartesMichelinFile.HeaderSize} bytes are needed, got {data.Length}).");

    var tileWidth = BinaryPrimitives.ReadInt32LittleEndian(data);
    var tileHeight = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
    var across = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
    var down = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);

    if (tileWidth is < CartesMichelinFile.MinTileSize or > CartesMichelinFile.MaxTileSize
        || tileHeight is < CartesMichelinFile.MinTileSize or > CartesMichelinFile.MaxTileSize)
      throw new InvalidDataException($"A Cartes Michelin sheet states tiles of {tileWidth}x{tileHeight}.");

    if (across is < CartesMichelinFile.MinGridCount or > CartesMichelinFile.MaxGridCount
        || down is < CartesMichelinFile.MinGridCount or > CartesMichelinFile.MaxGridCount)
      throw new InvalidDataException($"A Cartes Michelin sheet states a grid of {across}x{down} tiles.");

    var directoryBytes = (long)across * down * CartesMichelinFile.DirectoryEntrySize;
    if (CartesMichelinFile.HeaderSize + directoryBytes > data.Length)
      throw new InvalidDataException("A Cartes Michelin sheet's tile directory reaches past the end of the file.");

    // Walk the directory once for the bounding box of the tiles that are actually there.
    int minColumn = across, minRow = down, maxColumn = -1, maxRow = -1;
    for (var row = 0; row < down; ++row)
    for (var column = 0; column < across; ++column) {
      if (_Tile(data, across, row, column).IsEmpty)
        continue;

      if (column < minColumn) minColumn = column;
      if (column > maxColumn) maxColumn = column;
      if (row < minRow) minRow = row;
      if (row > maxRow) maxRow = row;
    }

    if (maxColumn < 0)
      throw new InvalidDataException("A Cartes Michelin sheet carries no tile, so there is no picture in it.");

    var columns = maxColumn - minColumn + 1;
    var rows = maxRow - minRow + 1;
    var width = tileWidth * columns;
    var height = tileHeight * rows;
    var pixels = new byte[width * height * 3];
    var placed = 0;

    for (var row = minRow; row <= maxRow; ++row)
    for (var column = minColumn; column <= maxColumn; ++column) {
      var tile = _Tile(data, across, row, column);
      if (tile.IsEmpty)
        continue;

      var picture = GifFile.ToRawImage(GifReader.FromSpan(tile)).EnsureFormat(PixelFormat.Rgb24);
      _Blit(picture, pixels, width, height, (column - minColumn) * tileWidth, (row - minRow) * tileHeight);
      ++placed;
    }

    return new() {
      Width = width,
      Height = height,
      TileWidth = tileWidth,
      TileHeight = tileHeight,
      TileCount = placed,
      PixelData = pixels,
    };
  }

  /// <summary>The bytes of one grid position's tile, or empty where there is none.</summary>
  private static ReadOnlySpan<byte> _Tile(ReadOnlySpan<byte> data, int across, int row, int column) {
    var at = CartesMichelinFile.HeaderSize + (row * across + column) * CartesMichelinFile.DirectoryEntrySize;
    var offset = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
    if (length <= 0 || offset <= 0 || (long)offset + length > data.Length)
      return default;

    var tile = data.Slice(offset, length);
    return tile.Length >= CartesMichelinFile.TileSignature.Length
           && tile[..CartesMichelinFile.TileSignature.Length].SequenceEqual(CartesMichelinFile.TileSignature)
      ? tile
      : default;
  }

  private static void _Blit(RawImage tile, byte[] pixels, int width, int height, int left, int top) {
    for (var y = 0; y < tile.Height; ++y) {
      var targetRow = top + y;
      if (targetRow >= height)
        break;

      for (var x = 0; x < tile.Width; ++x) {
        var targetColumn = left + x;
        if (targetColumn >= width)
          break;

        var from = (y * tile.Width + x) * 3;
        var to = (targetRow * width + targetColumn) * 3;
        pixels[to] = tile.PixelData[from];
        pixels[to + 1] = tile.PixelData[from + 1];
        pixels[to + 2] = tile.PixelData[from + 2];
      }
    }
  }
}
