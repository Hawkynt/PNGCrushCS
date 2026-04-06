using System;
using System.IO;

namespace FileFormat.Awd;

/// <summary>Reads AWD (Microsoft Fax) image files from bytes, streams, or file paths.</summary>
public static class AwdReader {

  public static AwdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AWD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AwdFile FromStream(Stream stream) {
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

  public static AwdFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AwdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AwdHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid AWD file.");

    var magic = data.AsSpan(0, 4);
    if (!magic.SequenceEqual(AwdHeader.Magic))
      throw new InvalidDataException("Invalid AWD magic bytes.");

    var header = AwdHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0)
      throw new InvalidDataException($"Invalid AWD width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid AWD height: {height}.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < AwdHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {AwdHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(AwdHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new AwdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
