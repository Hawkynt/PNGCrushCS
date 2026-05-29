using System;
using System.IO;

namespace FileFormat.Cmu;

/// <summary>Reads CMU Window Manager Bitmap files from bytes, streams, or file paths.</summary>
public static class CmuReader {

  public static CmuFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CMU file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CmuFile FromStream(Stream stream) {
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

  public static CmuFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CmuHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid CMU file.");

    var header = CmuHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid CMU width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid CMU height: {height}.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < CmuHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {CmuHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(CmuHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new CmuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static CmuFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CmuHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid CMU file.");

    var header = CmuHeader.ReadFrom(data.AsSpan());
    var width = header.Width;
    var height = header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid CMU width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid CMU height: {height}.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < CmuHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {CmuHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(CmuHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new CmuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
