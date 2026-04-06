using System;
using System.IO;

namespace FileFormat.GeoPaint;

/// <summary>Reads GEOS GeoPaint files from bytes, streams, or file paths.</summary>
public static class GeoPaintReader {

  /// <summary>Minimum file size: at least one scanline's worth of data (one end marker byte).</summary>
  private const int _MIN_SIZE = 1;

  public static GeoPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GeoPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GeoPaintFile FromStream(Stream stream) {
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

  public static GeoPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GeoPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException($"Data too small for a valid GeoPaint file: expected at least {_MIN_SIZE} byte(s), got {data.Length}.");

    // Determine height by decompressing scanlines until data is exhausted
    var offset = 0;
    var rowCount = 0;
    while (offset < data.Length && rowCount < GeoPaintFile.MaxHeight) {
      var before = offset;
      GeoPaintRleCompressor.DecompressScanline(data, ref offset, GeoPaintFile.BytesPerRow, out var bytesDecoded);

      // If offset didn't advance, the data is malformed or exhausted
      if (offset <= before)
        break;

      // A bare end marker (0xFF) with no pixel data decoded is not a valid scanline;
      // treat it as end-of-data and stop.
      if (bytesDecoded == 0)
        break;

      ++rowCount;
    }

    if (rowCount < 1)
      throw new InvalidDataException("GeoPaint data contains no decompressible scanlines.");

    // Now decompress fully with known height
    var pixelData = GeoPaintRleCompressor.Decompress(data, rowCount);

    return new GeoPaintFile {
      Height = rowCount,
      PixelData = pixelData,
    };
  }
}
