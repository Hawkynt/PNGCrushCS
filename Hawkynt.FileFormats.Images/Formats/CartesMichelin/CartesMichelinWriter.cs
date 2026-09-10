using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.CartesMichelin;

/// <summary>Writes Cartes Michelin road-atlas sheets (.big) as a directory of complete GIF tiles.</summary>
/// <remarks>
/// The format has no independent canvas size: the decoded dimensions are the bounding box of its
/// present directory entries multiplied by the stated tile size. Consequently this writer only
/// accepts dimensions that can be represented exactly by one to sixty-four equally sized tiles on
/// each axis. When an axis needs only one occupied tile the directory still contains the format's
/// minimum two positions and leaves the extra one absent.
/// </remarks>
public static class CartesMichelinWriter {

  public static byte[] ToBytes(CartesMichelinFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No Cartes Michelin picture to write.");
    if (file.Width < 1 || file.Height < 1)
      throw new InvalidOperationException($"A Cartes Michelin picture of {file.Width}x{file.Height} cannot be written.");

    var (tileWidth, occupiedColumns) = _AxisLayout(file.Width, file.TileWidth);
    var (tileHeight, occupiedRows) = _AxisLayout(file.Height, file.TileHeight);
    var gridColumns = _GridCount(file.GridColumns, occupiedColumns);
    var gridRows = _GridCount(file.GridRows, occupiedRows);

    var required = (long)file.Width * file.Height * 3;
    if (required > Array.MaxLength || file.PixelData.Length < required)
      throw new InvalidOperationException(
        $"A {file.Width}x{file.Height} Cartes Michelin picture needs {required} RGB bytes and {file.PixelData.Length} were given.");

    var tileCount = checked(occupiedColumns * occupiedRows);
    var tiles = new byte[tileCount][];
    var directoryBytes = checked(gridColumns * gridRows * CartesMichelinFile.DirectoryEntrySize);
    long outputLength = CartesMichelinFile.HeaderSize + directoryBytes;

    for (var row = 0; row < occupiedRows; ++row)
    for (var column = 0; column < occupiedColumns; ++column) {
      var tilePixels = _ExtractTile(file, row, column, tileWidth, tileHeight);
      var image = new RawImage {
        Width = tileWidth,
        Height = tileHeight,
        Format = PixelFormat.Rgb24,
        PixelData = tilePixels,
      };
      var gif = GifWriter.ToBytes(GifFile.FromRawImage(image));
      tiles[row * occupiedColumns + column] = gif;
      outputLength += gif.Length;
      if (outputLength > Array.MaxLength || outputLength > int.MaxValue)
        throw new InvalidDataException("The encoded Cartes Michelin sheet is too large for its 32-bit directory offsets.");
    }

    var result = new byte[(int)outputLength];
    BinaryPrimitives.WriteInt32LittleEndian(result, tileWidth);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), tileHeight);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), gridColumns);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), gridRows);

    var payloadAt = CartesMichelinFile.HeaderSize + directoryBytes;
    for (var row = 0; row < gridRows; ++row)
    for (var column = 0; column < gridColumns; ++column) {
      var entryAt = CartesMichelinFile.HeaderSize
                    + (row * gridColumns + column) * CartesMichelinFile.DirectoryEntrySize;
      if (row >= occupiedRows || column >= occupiedColumns)
        continue;

      var tile = tiles[row * occupiedColumns + column];
      BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(entryAt), payloadAt);
      BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(entryAt + 4), tile.Length);
      tile.CopyTo(result, payloadAt);
      payloadAt += tile.Length;
    }

    return result;
  }

  private static (int TileSize, int OccupiedCount) _AxisLayout(int length, int preferredTileSize) {
    if (preferredTileSize is >= CartesMichelinFile.MinTileSize and <= CartesMichelinFile.MaxTileSize
        && length % preferredTileSize == 0) {
      var count = length / preferredTileSize;
      if (count is >= 1 and <= CartesMichelinFile.MaxGridCount)
        return (preferredTileSize, count);
    }

    if (CartesMichelinFile.TryGetAxisLayout(length, out var tileSize, out var occupiedCount))
      return (tileSize, occupiedCount);

    throw new InvalidOperationException(
      $"An axis of {length} pixels cannot be represented exactly by 1..{CartesMichelinFile.MaxGridCount} tiles "
      + $"of {CartesMichelinFile.MinTileSize}..{CartesMichelinFile.MaxTileSize} pixels.");
  }

  private static int _GridCount(int preferred, int occupied)
    => preferred is >= CartesMichelinFile.MinGridCount and <= CartesMichelinFile.MaxGridCount && preferred >= occupied
      ? preferred
      : Math.Max(CartesMichelinFile.MinGridCount, occupied);

  private static byte[] _ExtractTile(
    CartesMichelinFile file, int row, int column, int tileWidth, int tileHeight) {
    var result = new byte[checked(tileWidth * tileHeight * 3)];
    var rowBytes = tileWidth * 3;
    var left = column * tileWidth;
    var top = row * tileHeight;

    for (var y = 0; y < tileHeight; ++y) {
      var sourceAt = ((top + y) * file.Width + left) * 3;
      file.PixelData.AsSpan(sourceAt, rowBytes).CopyTo(result.AsSpan(y * rowBytes, rowBytes));
    }

    return result;
  }
}
