using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.PortfolioGraphics;

/// <summary>Reads Atari Portfolio Graphics (PGF/PGC) images from bytes, streams, or file paths.</summary>
public static class PortfolioGraphicsReader {

  public static PortfolioGraphicsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Portfolio Graphics file not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);
    var ext = file.Extension.ToLowerInvariant();
    return ext == ".pgc" ? _ParsePgc(data) : _ParsePgf(data);
  }

  public static PortfolioGraphicsFile FromStream(Stream stream) {
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

  public static PortfolioGraphicsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PortfolioGraphicsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PortfolioGraphicsFile.PgfHeaderSize + PortfolioGraphicsFile.PixelDataSize)
      return _ParsePgc(data);
    return _ParsePgf(data);
  }

  private static PortfolioGraphicsFile _ParsePgf(byte[] data) {
    if (data.Length < PortfolioGraphicsFile.PgfHeaderSize + PortfolioGraphicsFile.PixelDataSize)
      throw new InvalidDataException($"PGF data too small: expected at least {PortfolioGraphicsFile.PgfHeaderSize + PortfolioGraphicsFile.PixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[PortfolioGraphicsFile.PixelDataSize];
    data.AsSpan(PortfolioGraphicsFile.PgfHeaderSize, PortfolioGraphicsFile.PixelDataSize).CopyTo(pixelData.AsSpan(0));
    return new() { PixelData = pixelData };
  }

  private static PortfolioGraphicsFile _ParsePgc(byte[] data) {
    if (data.Length < 2)
      throw new InvalidDataException($"PGC data too small: expected at least 2 bytes, got {data.Length}.");

    var output = new List<byte>(PortfolioGraphicsFile.PixelDataSize);
    var pos = 0;

    while (pos < data.Length && output.Count < PortfolioGraphicsFile.PixelDataSize) {
      var b = data[pos++];
      if (b == 0x00 && pos < data.Length) {
        // RLE: next byte is count, then value
        if (pos + 1 >= data.Length)
          break;
        var count = data[pos++];
        var value = data[pos++];
        for (var i = 0; i < count && output.Count < PortfolioGraphicsFile.PixelDataSize; ++i)
          output.Add(value);
      } else
        output.Add(b);
    }

    while (output.Count < PortfolioGraphicsFile.PixelDataSize)
      output.Add(0);

    var pixelData = new byte[PortfolioGraphicsFile.PixelDataSize];
    output.CopyTo(0, pixelData, 0, PortfolioGraphicsFile.PixelDataSize);
    return new() { PixelData = pixelData };
  }
}
