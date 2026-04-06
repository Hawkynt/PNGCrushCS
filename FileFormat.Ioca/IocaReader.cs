using System;
using System.IO;

namespace FileFormat.Ioca;

/// <summary>Reads IOCA images from bytes, streams, or file paths (simplified structured field parsing).</summary>
public static class IocaReader {

  // Simplified IOCA structured field IDs
  private const byte SfIntroducer = 0x5A;

  public static IocaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IOCA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IocaFile FromStream(Stream stream) {
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

  public static IocaFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static IocaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < IocaFile.MinHeaderSize)
      throw new InvalidDataException($"IOCA data too small: expected at least {IocaFile.MinHeaderSize} bytes, got {data.Length}.");

    // Parse simplified IOCA container
    // Header: 2-byte length prefix, SF introducer, field data
    var pos = 0;

    // Try to find image dimensions and pixel data
    var width = 0;
    var height = 0;
    byte[]? pixelData = null;

    while (pos + 2 < data.Length) {
      if (data[pos] == SfIntroducer) {
        // Structured field: introducer + 2-byte length (BE)
        if (pos + 3 >= data.Length)
          break;
        var sfLen = (data[pos + 1] << 8) | data[pos + 2];
        if (sfLen < 3)
          break;

        // Check for image size triplet (simplified)
        if (pos + 7 < data.Length && width == 0) {
          width = (data[pos + 3] << 8) | data[pos + 4];
          height = (data[pos + 5] << 8) | data[pos + 6];
        }

        pos += sfLen;
      } else {
        // Try raw length-prefixed record
        var recLen = (data[pos] << 8) | data[pos + 1];
        if (recLen < 2 || pos + recLen > data.Length)
          break;

        // Look for image data after header fields
        if (width > 0 && height > 0 && pixelData == null) {
          var bytesPerRow = (width + 7) / 8;
          var expectedPixelSize = bytesPerRow * height;
          var dataStart = pos + 2;
          var available = recLen - 2;
          if (available >= expectedPixelSize) {
            pixelData = new byte[expectedPixelSize];
            data.AsSpan(dataStart, expectedPixelSize).CopyTo(pixelData.AsSpan(0));
          }
        }

        pos += recLen;
      }
    }

    // Fallback: if no structured fields parsed, treat as raw with dimensions in first 4 bytes
    if (width == 0 || height == 0) {
      if (data.Length >= 4) {
        width = (data[0] << 8) | data[1];
        height = (data[2] << 8) | data[3];
      }

      if (width <= 0 || height <= 0)
        throw new InvalidDataException("Could not determine image dimensions from IOCA data.");

      var bytesPerRow = (width + 7) / 8;
      var expectedSize = bytesPerRow * height;
      pixelData = new byte[expectedSize];
      var dataOffset = 4;
      var copyLen = Math.Min(data.Length - dataOffset, expectedSize);
      if (copyLen > 0)
        data.AsSpan(dataOffset, copyLen).CopyTo(pixelData.AsSpan(0));
    }

    return new() { Width = width, Height = height, PixelData = pixelData ?? [] };
  }
}
