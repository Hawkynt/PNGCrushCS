using System;
using System.IO;

namespace FileFormat.FmTowns;

/// <summary>Parses fujitsu fm towns 256-color screen dump from raw bytes.</summary>
public static class FmTownsReader {

  public static FmTownsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FmTownsFile FromStream(Stream stream) {
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

  public static FmTownsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FmTownsFile.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 64000.");

    var pixelData = new byte[FmTownsFile.ImageWidth * FmTownsFile.ImageHeight];
    data.AsSpan(0, pixelData.Length).CopyTo(pixelData.AsSpan(0));

    return new FmTownsFile { PixelData = pixelData };
  }
}
