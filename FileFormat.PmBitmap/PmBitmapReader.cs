using System;
using System.IO;

namespace FileFormat.PmBitmap;

/// <summary>Reads PM bitmap image files from bytes, streams, or file paths.</summary>
public static class PmBitmapReader {

  public static PmBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PM bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PmBitmapFile FromStream(Stream stream) {
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

  public static PmBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PmBitmapFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid PM bitmap file: expected at least {PmBitmapFile.HeaderSize} bytes, got {data.Length}.");

    if (data[0] != (byte)'P' || data[1] != (byte)'M' || data[2] != 0)
      throw new InvalidDataException("Invalid PM bitmap magic: expected 'PM\0'.");

    var version = data[3];
    var width = data[4] | (data[5] << 8);
    var height = data[6] | (data[7] << 8);
    var depth = data[8] | (data[9] << 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid PM bitmap width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid PM bitmap height: {height}.");
    if (depth != 8 && depth != 24)
      throw new InvalidDataException($"Invalid PM bitmap depth: {depth}. Expected 8 or 24.");

    var bytesPerPixel = depth / 8;
    var expectedPixelBytes = width * height * bytesPerPixel;
    if (data.Length < PmBitmapFile.HeaderSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {PmBitmapFile.HeaderSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(PmBitmapFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new PmBitmapFile {
      Width = width,
      Height = height,
      Depth = depth,
      Version = version,
      PixelData = pixelData,
    };
  }
}
