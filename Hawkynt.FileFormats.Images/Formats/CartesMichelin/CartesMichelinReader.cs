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

  /// <summary>
  /// Identifies a sheet by the structure around its embedded GIF tiles rather than by the four
  /// range-limited integers at its front. <c>null</c> means the supplied prefix ends before a
  /// present tile's signature can be inspected.
  /// </summary>
  internal static bool? MatchesSignature(ReadOnlySpan<byte> data) {
    if (data.Length < CartesMichelinFile.HeaderSize)
      return null;

    var tileWidth = BinaryPrimitives.ReadInt32LittleEndian(data);
    var tileHeight = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
    var across = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
    var down = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
    if (!_HeaderIsPlausible(tileWidth, tileHeight, across, down))
      return false;

    var directoryEnd = CartesMichelinFile.HeaderSize
                       + (long)across * down * CartesMichelinFile.DirectoryEntrySize;
    if (directoryEnd > data.Length)
      return null;

    var needsMoreData = false;
    for (var index = 0; index < across * down; ++index) {
      var at = CartesMichelinFile.HeaderSize + index * CartesMichelinFile.DirectoryEntrySize;
      var offset = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
      if (length == 0)
        continue;
      if (length < CartesMichelinFile.TileSignature.Length || offset <= 0)
        continue;

      var signatureEnd = (long)offset + CartesMichelinFile.TileSignature.Length;
      if (signatureEnd > data.Length) {
        needsMoreData = true;
        continue;
      }

      if (data.Slice(offset, CartesMichelinFile.TileSignature.Length)
          .SequenceEqual(CartesMichelinFile.TileSignature))
        return true;
    }

    return needsMoreData ? null : false;
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
    var width = checked(tileWidth * columns);
    var height = checked(tileHeight * rows);
    var pixelBytes = (long)width * height * 3;
    if (pixelBytes > Array.MaxLength)
      throw new InvalidDataException($"The assembled Cartes Michelin sheet is too large ({width}x{height}).");

    var pixels = new byte[(int)pixelBytes];
    var placed = 0;

    for (var row = minRow; row <= maxRow; ++row)
    for (var column = minColumn; column <= maxColumn; ++column) {
      var tile = _Tile(data, across, row, column);
      if (tile.IsEmpty)
        continue;

      var picture = GifFile.ToRawImage(GifReader.FromSpan(tile)).EnsureFormat(PixelFormat.Rgb24);
      _Blit(
        picture, pixels, width,
        (column - minColumn) * tileWidth, (row - minRow) * tileHeight,
        tileWidth, tileHeight);
      ++placed;
    }

    return new() {
      Width = width,
      Height = height,
      TileWidth = tileWidth,
      TileHeight = tileHeight,
      GridColumns = across,
      GridRows = down,
      TileCount = placed,
      PixelData = pixels,
    };
  }

  private static bool _HeaderIsPlausible(int tileWidth, int tileHeight, int across, int down)
    => tileWidth is >= CartesMichelinFile.MinTileSize and <= CartesMichelinFile.MaxTileSize
       && tileHeight is >= CartesMichelinFile.MinTileSize and <= CartesMichelinFile.MaxTileSize
       && across is >= CartesMichelinFile.MinGridCount and <= CartesMichelinFile.MaxGridCount
       && down is >= CartesMichelinFile.MinGridCount and <= CartesMichelinFile.MaxGridCount;

  /// <summary>The bytes of one grid position's tile, or empty where the directory marks it absent.</summary>
  private static ReadOnlySpan<byte> _Tile(ReadOnlySpan<byte> data, int across, int row, int column) {
    var at = CartesMichelinFile.HeaderSize + (row * across + column) * CartesMichelinFile.DirectoryEntrySize;
    var offset = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
    if (length == 0)
      return default;
    if (length < 0 || offset <= 0 || (long)offset + length > data.Length)
      throw new InvalidDataException($"Cartes Michelin tile {column},{row} has an invalid offset/length entry ({offset}, {length}).");

    var tile = data.Slice(offset, length);
    if (tile.Length < CartesMichelinFile.TileSignature.Length
        || !tile[..CartesMichelinFile.TileSignature.Length].SequenceEqual(CartesMichelinFile.TileSignature))
      throw new InvalidDataException($"Cartes Michelin tile {column},{row} is present but does not contain a GIF image.");

    return tile;
  }

  private static void _Blit(
    RawImage tile, byte[] pixels, int width,
    int left, int top, int slotWidth, int slotHeight) {
    var copyHeight = Math.Min(tile.Height, slotHeight);
    var copyWidth = Math.Min(tile.Width, slotWidth);
    for (var y = 0; y < copyHeight; ++y) {
      for (var x = 0; x < copyWidth; ++x) {
        var from = (y * tile.Width + x) * 3;
        var to = ((top + y) * width + left + x) * 3;
        pixels[to] = tile.PixelData[from];
        pixels[to + 1] = tile.PixelData[from + 1];
        pixels[to + 2] = tile.PixelData[from + 2];
      }
    }
  }
}
