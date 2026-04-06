using System;
using System.IO;

namespace FileFormat.Wbmp;

/// <summary>Reads WBMP files from bytes, streams, or file paths.</summary>
public static class WbmpReader {

  public static WbmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WBMP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WbmpFile FromStream(Stream stream) {
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

  public static WbmpFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static WbmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid WBMP file.");

    var span = data.AsSpan();

    // Type byte must be 0 (Type 0 WBMP)
    if (span[0] != 0)
      throw new InvalidDataException("Invalid WBMP type byte; only type 0 is supported.");

    // FixedHeader byte (reserved, must be 0)
    var offset = 2;

    // Decode width
    var width = WbmpMultiByteInt.Decode(span[offset..], out var widthBytes);
    offset += widthBytes;

    // Decode height
    if (offset >= data.Length)
      throw new InvalidDataException("Data too small: missing height.");

    var height = WbmpMultiByteInt.Decode(span[offset..], out var heightBytes);
    offset += heightBytes;

    // Read pixel data
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var remaining = data.Length - offset;

    if (remaining < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedPixelBytes} bytes, got {remaining}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(offset, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new WbmpFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
