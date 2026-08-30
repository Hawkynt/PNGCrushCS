using System;
using System.IO;

namespace FileFormat.Ybm;

/// <summary>Reads Bennet Yee face-file bitmaps (YBM).</summary>
public static class YbmReader {

  private const int _HeaderSize = 6;

  public static YbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("YBM file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static YbmFile FromStream(Stream stream) {
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

  public static YbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static YbmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HeaderSize)
      throw new InvalidDataException("Truncated YBM header.");
    if (data[0] != 0x21 || data[1] != 0x21)
      throw new InvalidDataException("Invalid YBM magic; expected '!!'.");

    var width = (short)((data[2] << 8) | data[3]);
    var height = (short)((data[4] << 8) | data[5]);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException("YBM dimensions must be positive signed 16-bit values.");

    var rasterLength = checked(YbmFile.GetRowStride(width) * height);
    if (data.Length - _HeaderSize < rasterLength)
      throw new InvalidDataException($"Truncated YBM raster: expected {rasterLength} bytes, got {data.Length - _HeaderSize}.");
    if (data.Length - _HeaderSize > rasterLength)
      throw new InvalidDataException($"Unexpected trailing YBM data: expected {rasterLength} raster bytes, got {data.Length - _HeaderSize}.");

    return new YbmFile {
      Width = width,
      Height = height,
      RasterData = data[_HeaderSize..].ToArray(),
    };
  }
}
