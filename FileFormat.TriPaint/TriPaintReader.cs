using System;
using System.IO;

namespace FileFormat.TriPaint;

/// <summary>Reads TriPaint screen dumps from bytes, streams, or file paths.</summary>
public static class TriPaintReader {

  /// <summary>The exact file size of a valid TriPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = TriPaintFile.ExpectedFileSize;

  public static TriPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TriPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TriPaintFile FromStream(Stream stream) {
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

  public static TriPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid TriPaint data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.Slice(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new TriPaintFile {
      PixelData = pixelData
    };
    }

  public static TriPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid TriPaint data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new TriPaintFile {
      PixelData = pixelData
    };
  }
}
