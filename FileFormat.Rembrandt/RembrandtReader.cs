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

  public static RembrandtFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static RembrandtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RembrandtFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Rembrandt file (minimum {RembrandtFile.MinFileSize} bytes, got {data.Length}).");

    // Read dimensions (BE u16)
    var width = (ushort)((data[0] << 8) | data[1]);
    var height = (ushort)((data[2] << 8) | data[3]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Rembrandt dimensions: {width}x{height}.");

    // Read pixel data
    var pixelOffset = RembrandtFile.HeaderSize;
    var expectedPixelBytes = width * height * 2;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(pixelOffset, copyLen).CopyTo(pixelData);

    return new RembrandtFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
