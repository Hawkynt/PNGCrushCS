using System;
using System.IO;

namespace FileFormat.NokiaGroupGraphics;

/// <summary>Reads Nokia Group Graphics NGG files from bytes, streams, or file paths.</summary>
public static class NokiaGroupGraphicsReader {

  public static NokiaGroupGraphicsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NGG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaGroupGraphicsFile FromStream(Stream stream) {
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

  public static NokiaGroupGraphicsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NokiaGroupGraphicsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NokiaGroupGraphicsFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid NGG file (need at least {NokiaGroupGraphicsFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != NokiaGroupGraphicsFile.Magic[0] || data[1] != NokiaGroupGraphicsFile.Magic[1] || data[2] != NokiaGroupGraphicsFile.Magic[2])
      throw new InvalidDataException("Invalid NGG magic bytes.");

    var version = data[3];
    var width = (int)data[4];
    var height = (int)data[5];

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid NGG dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < NokiaGroupGraphicsFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("NGG file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(NokiaGroupGraphicsFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
