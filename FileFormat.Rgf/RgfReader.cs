using System;
using System.IO;

namespace FileFormat.Rgf;

/// <summary>Reads RGF (LEGO Mindstorms EV3) files from bytes, streams, or file paths.</summary>
public static class RgfReader {

  /// <summary>Minimum valid RGF file size: 2-byte header + at least 1 byte of pixel data.</summary>
  private const int _HEADER_SIZE = 2;

  public static RgfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RGF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RgfFile FromStream(Stream stream) {
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

  public static RgfFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static RgfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid RGF file.");

    var width = data[0];
    var height = data[1];

    if (width == 0)
      throw new InvalidDataException("Invalid RGF width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid RGF height: 0.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var expectedFileSize = _HEADER_SIZE + expectedPixelBytes;

    if (data.Length < expectedFileSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(_HEADER_SIZE, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new RgfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
