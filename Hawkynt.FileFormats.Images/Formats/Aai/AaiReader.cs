using System;
using System.IO;

namespace FileFormat.Aai;

/// <summary>Reads AAI (Dune HD) files from bytes, streams, or file paths.</summary>
public static class AaiReader {

  public static AaiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AAI file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static AaiFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AaiFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AaiHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid AAI file.");

    var header = AaiHeader.ReadFrom(data);
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid AAI width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid AAI height: {height}.");

    var expectedPixelBytes = width * height * 4;
    if (data.Length - AaiHeader.StructSize != expectedPixelBytes)
      throw new InvalidDataException($"Invalid AAI data size: expected {AaiHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    return new AaiFile {
      Width = width,
      Height = height,
      PixelData = data.Slice(AaiHeader.StructSize, expectedPixelBytes).ToArray()
    };
  }

  public static AaiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
