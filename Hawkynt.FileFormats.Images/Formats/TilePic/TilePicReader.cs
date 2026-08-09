using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.TilePic;

/// <summary>Reads TilePic images from bytes, streams, or file paths.</summary>
public static class TilePicReader {

  /// <summary>What a JPEG opens with, which is what a <c>.tjp</c> tile has to be.</summary>
  private static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];

  public static TilePicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TilePic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TilePicFile FromStream(Stream stream) {
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

  public static TilePicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static TilePicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < TilePicFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a TilePic file (minimum {TilePicFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..TilePicFile.Signature.Length].SequenceEqual(TilePicFile.Signature))
      throw new InvalidDataException("Not a TilePic file: it does not begin with TPC.");

    var headerSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    if (headerSize != TilePicFile.HeaderSize)
      throw new InvalidDataException($"A TilePic file states a header of {headerSize} bytes; the format's is {TilePicFile.HeaderSize}.");

    var imageWidth = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
    var imageHeight = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
    var tileWidth = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
    var tileHeight = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
    var tileCount = BinaryPrimitives.ReadUInt32BigEndian(data[24..]);
    var layerCount = BinaryPrimitives.ReadUInt16BigEndian(data[28..]);
    var scale = BinaryPrimitives.ReadUInt16BigEndian(data[30..]);
    var attributeBytes = BinaryPrimitives.ReadUInt32BigEndian(data[32..]);

    if (imageWidth < 1 || imageHeight < 1 || imageWidth > int.MaxValue || imageHeight > int.MaxValue)
      throw new InvalidDataException($"A TilePic file states a picture of {imageWidth}x{imageHeight}.");

    if (tileWidth < 1 || tileHeight < 1)
      throw new InvalidDataException($"A TilePic file states tiles of {tileWidth}x{tileHeight}.");

    if (layerCount < 1)
      throw new InvalidDataException("A TilePic file states no layers.");

    // One layer needs no scale; more than one needs a real one, or every layer would be the same
    // size and the tile count could never come out.
    if (layerCount > 1 && scale < 2)
      throw new InvalidDataException($"A TilePic file states {layerCount} layers at a scale of {scale}.");

    if (tileCount < 1 || tileCount > TilePicFile.MaximumTiles)
      throw new InvalidDataException($"A TilePic file states {tileCount} tiles.");

    var indexBytes = ((long)tileCount + 1) * TilePicFile.IndexEntrySize;
    if (TilePicFile.HeaderSize + indexBytes > data.Length)
      throw new InvalidDataException($"A TilePic file states {tileCount} tiles, whose index does not fit in {data.Length} bytes.");

    // Where each layer's tiles start in the index, and how many across and down it has. The layers
    // have to account for the tile count exactly: that is the one arithmetic check the format
    // affords, and a file that fails it is not being read the way it was written.
    var layers = new (int Across, int Down, int First)[layerCount];
    var counted = 0L;
    for (var layer = 0; layer < layerCount; ++layer) {
      var reduction = 1L;
      for (var i = layer; i < layerCount - 1; ++i) {
        reduction *= scale;
        if (reduction > imageWidth && reduction > imageHeight)
          break;
      }

      var layerWidth = (imageWidth + reduction - 1) / reduction;
      var layerHeight = (imageHeight + reduction - 1) / reduction;
      var across = (int)((layerWidth + tileWidth - 1) / tileWidth);
      var down = (int)((layerHeight + tileHeight - 1) / tileHeight);
      layers[layer] = (across, down, (int)counted);
      counted += (long)across * down;
    }

    if (counted != tileCount)
      throw new InvalidDataException(
        $"A TilePic file of {imageWidth}x{imageHeight} in tiles of {tileWidth}x{tileHeight} over {layerCount} layers at scale {scale} needs {counted} tiles and states {tileCount}.");

    var offsets = new long[tileCount + 1];
    for (var i = 0; i <= tileCount; ++i)
      offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(data[(TilePicFile.HeaderSize + i * TilePicFile.IndexEntrySize)..]);

    if (offsets[0] < TilePicFile.HeaderSize + indexBytes)
      throw new InvalidDataException($"A TilePic file puts its first tile at {offsets[0]}, inside its own index.");

    for (var i = 1; i <= tileCount; ++i)
      if (offsets[i] < offsets[i - 1])
        throw new InvalidDataException($"A TilePic file states tile {i} at {offsets[i]}, behind tile {i - 1} at {offsets[i - 1]}.");

    // The attributes start where the last tile ends and run to the end of the file. That is the
    // stated length accounting for the file, which is what identifies this as the format rather
    // than four bytes that happen to read as TPC.
    if (offsets[tileCount] + attributeBytes != data.Length)
      throw new InvalidDataException(
        $"A TilePic file ends its tiles at {offsets[tileCount]} with {attributeBytes} bytes of attributes, which does not account for a file of {data.Length}.");

    var (bottomAcross, bottomDown, bottomFirst) = layers[layerCount - 1];
    var width = (int)imageWidth;
    var height = (int)imageHeight;
    var pixels = new byte[(long)width * height * 3 is var wanted && wanted <= int.MaxValue
      ? (int)wanted
      : throw new InvalidDataException($"A TilePic picture of {width}x{height} is larger than can be held.")];

    for (var row = 0; row < bottomDown; ++row)
    for (var column = 0; column < bottomAcross; ++column) {
      var index = bottomFirst + row * bottomAcross + column;
      var start = offsets[index];
      var length = offsets[index + 1] - start;
      if (length < JpegStart.Length)
        throw new InvalidDataException($"Tile {index + 1} of a TilePic file holds {length} bytes.");

      var tile = data.Slice((int)start, (int)length);
      if (!tile[..JpegStart.Length].SequenceEqual(JpegStart))
        throw new InvalidDataException($"Tile {index + 1} of a TilePic file is not a JPEG.");

      var decoded = PixelConverter.Convert(JpegFile.ToRawImage(JpegReader.FromBytes(tile.ToArray())), PixelFormat.Rgb24);

      var left = column * (int)tileWidth;
      var top = row * (int)tileHeight;
      var expectedWide = Math.Min((int)tileWidth, width - left);
      var expectedHigh = Math.Min((int)tileHeight, height - top);

      // A tile is either cut to the edge of the picture or padded out to a whole tile; the format
      // says it does not settle which, so both are taken and anything else is refused.
      if ((decoded.Width != expectedWide && decoded.Width != (int)tileWidth)
          || (decoded.Height != expectedHigh && decoded.Height != (int)tileHeight))
        throw new InvalidDataException(
          $"Tile {index + 1} of a TilePic file is {decoded.Width}x{decoded.Height} where the picture wants {expectedWide}x{expectedHigh}.");

      for (var y = 0; y < expectedHigh; ++y) {
        var from = y * decoded.Width * 3;
        var to = ((top + y) * width + left) * 3;
        decoded.PixelData.AsSpan(from, expectedWide * 3).CopyTo(pixels.AsSpan(to));
      }
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }
}
