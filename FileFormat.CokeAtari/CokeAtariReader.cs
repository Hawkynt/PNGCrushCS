using System;
using System.IO;

namespace FileFormat.CokeAtari;

/// <summary>Reads COKE Atari Falcon 16-bit true color files from bytes, streams, or file paths.</summary>
public static class CokeAtariReader {

  public static CokeAtariFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("COKE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CokeAtariFile FromStream(Stream stream) {
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

  public static CokeAtariFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CokeAtariFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CokeAtariHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid COKE file.");

    var header = CokeAtariHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("COKE image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 2;
    var available = data.Length - CokeAtariHeader.StructSize;
    if (available < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {CokeAtariHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(CokeAtariHeader.StructSize, expectedPixelBytes).CopyTo(pixelData);

    return new CokeAtariFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
