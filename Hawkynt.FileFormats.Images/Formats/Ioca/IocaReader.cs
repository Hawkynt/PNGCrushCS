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

  public static IocaFile FromSpan(ReadOnlySpan<byte> data) {

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
            data.Slice(dataStart, expectedPixelSize).CopyTo(pixelData.AsSpan(0));
          }
        }

        pos += recLen;
      }
    }

    // No fallback. This used to take any file's first four bytes as a width and a height when it
    // found no structured field, so every file of four bytes or more was drawn as something — which
    // is not a lenient reader, it is a reader that cannot say no. A document that states no size in
    // the fields the format defines is not one this can read.
    if (width <= 0 || height <= 0 || pixelData == null)
      throw new InvalidDataException(
        "Not an IOCA image: no structured field in it states an image size, and the size is not guessed.");

    return new() { Width = width, Height = height, PixelData = pixelData ?? [] };
    }

  public static IocaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
