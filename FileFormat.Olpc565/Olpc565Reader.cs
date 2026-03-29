using System;
using System.IO;

namespace FileFormat.Olpc565;

/// <summary>Reads OLPC RGB565 (.565) files from bytes, streams, or file paths.</summary>
public static class Olpc565Reader {

  public static Olpc565File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("565 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Olpc565File FromStream(Stream stream) {
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

  public static Olpc565File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Olpc565Header.StructSize)
      throw new InvalidDataException("Data too small for a valid 565 file.");

    var header = Olpc565Header.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid 565 width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid 565 height: {height}.");

    var expectedPixelBytes = width * height * 2;

    if (data.Length < Olpc565Header.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {Olpc565Header.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(Olpc565Header.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new Olpc565File {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
