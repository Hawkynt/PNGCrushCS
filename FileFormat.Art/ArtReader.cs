using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Art;

/// <summary>Reads Build Engine ART tile archive files from bytes, streams, or file paths.</summary>
public static class ArtReader {

  public static ArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ART file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArtFile FromStream(Stream stream) {
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

  public static ArtFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ArtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid ART file.");

    var span = data.AsSpan();
    var header = ArtHeader.ReadFrom(span);

    if (header.Version != 1)
      throw new InvalidDataException($"Unsupported ART version {header.Version}, expected 1.");

    var numTiles = header.TileEnd - header.TileStart + 1;
    if (numTiles < 0)
      throw new InvalidDataException("TileEnd is less than TileStart.");

    var arraysOffset = ArtHeader.StructSize;
    var requiredForArrays = arraysOffset + numTiles * 2 + numTiles * 2 + numTiles * 4;
    if (data.Length < requiredForArrays)
      throw new InvalidDataException("Data too small for tile width/height/animation arrays.");

    var widthsOffset = arraysOffset;
    var heightsOffset = widthsOffset + numTiles * 2;
    var picanmOffset = heightsOffset + numTiles * 2;

    var widths = new short[numTiles];
    var heights = new short[numTiles];
    var picanms = new int[numTiles];

    for (var i = 0; i < numTiles; ++i) {
      widths[i] = BinaryPrimitives.ReadInt16LittleEndian(span[(widthsOffset + i * 2)..]);
      heights[i] = BinaryPrimitives.ReadInt16LittleEndian(span[(heightsOffset + i * 2)..]);
      picanms[i] = BinaryPrimitives.ReadInt32LittleEndian(span[(picanmOffset + i * 4)..]);
    }

    var pixelOffset = picanmOffset + numTiles * 4;
    var tiles = new List<ArtTile>(numTiles);

    for (var i = 0; i < numTiles; ++i) {
      var w = widths[i];
      var h = heights[i];
      var picanm = picanms[i];

      var numFrames = picanm & 0x3F;
      var animType = (ArtAnimType)((picanm >> 6) & 0x0F);
      var xOffset = (picanm >> 10) & 0x3F;
      var yOffset = (picanm >> 16) & 0xFF;
      var animSpeed = (picanm >> 24) & 0xFF;

      // Sign-extend 6-bit XOffset
      if (xOffset >= 32)
        xOffset -= 64;

      // Sign-extend 8-bit YOffset
      if (yOffset >= 128)
        yOffset -= 256;

      var totalPixels = w * h;
      var rowMajor = new byte[totalPixels];

      if (totalPixels > 0) {
        if (data.Length < pixelOffset + totalPixels)
          throw new InvalidDataException($"Data too small for tile {i} pixel data.");

        // Convert column-major to row-major
        for (var x = 0; x < w; ++x)
          for (var y = 0; y < h; ++y)
            rowMajor[y * w + x] = data[pixelOffset + x * h + y];

        pixelOffset += totalPixels;
      }

      tiles.Add(new ArtTile {
        Width = w,
        Height = h,
        AnimType = animType,
        NumFrames = numFrames,
        XOffset = xOffset,
        YOffset = yOffset,
        AnimSpeed = animSpeed,
        PixelData = rowMajor
      });
    }

    return new ArtFile { TileStart = header.TileStart, Tiles = tiles };
  }
}
