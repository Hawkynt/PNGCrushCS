using System;
using System.IO;

namespace FileFormat.Rembrandt;

/// <summary>Reads Atari Falcon Rembrandt true-color images from bytes, streams, or file paths.</summary>
public static class RembrandtReader {

  public static RembrandtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Rembrandt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RembrandtFile FromStream(Stream stream) {
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

  public static RembrandtFile FromSpan(ReadOnlySpan<byte> data) {

    if (!RembrandtHeader.TryRead(data, out var width, out var height))
      throw new InvalidDataException("Not a Rembrandt file: missing the 'TRUECOLR'/'PICT' header.");

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Rembrandt dimensions: {width}x{height}.");

    // Read pixel data
    var pixelOffset = RembrandtHeader.StructSize;
    var expectedPixelBytes = width * height * 2;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(pixelOffset, copyLen).CopyTo(pixelData);

    return new RembrandtFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static RembrandtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
