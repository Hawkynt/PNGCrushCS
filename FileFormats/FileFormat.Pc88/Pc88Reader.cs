using System;
using System.IO;

namespace FileFormat.Pc88;

/// <summary>Parses nec pc-88 monochrome graphics screen from raw bytes.</summary>
public static class Pc88Reader {

  public static Pc88File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pc88File FromStream(Stream stream) {
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

  public static Pc88File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Pc88File.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 16000.");

    var pixelData = new byte[Pc88File.ImageWidth * Pc88File.ImageHeight];
    for (var y = 0; y < Pc88File.ImageHeight; ++y)
      for (var x = 0; x < Pc88File.ImageWidth; x += 8) {
        var b = data[y * 80 + x / 8];
        for (var bit = 0; bit < 8 && x + bit < Pc88File.ImageWidth; ++bit)
          pixelData[y * Pc88File.ImageWidth + x + bit] = (byte)((b >> (7 - bit)) & 1);
      }

    return new Pc88File { PixelData = pixelData };
    }

  public static Pc88File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Pc88File.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 16000.");

    var pixelData = new byte[Pc88File.ImageWidth * Pc88File.ImageHeight];
    for (var y = 0; y < Pc88File.ImageHeight; ++y)
      for (var x = 0; x < Pc88File.ImageWidth; x += 8) {
        var b = data[y * 80 + x / 8];
        for (var bit = 0; bit < 8 && x + bit < Pc88File.ImageWidth; ++bit)
          pixelData[y * Pc88File.ImageWidth + x + bit] = (byte)((b >> (7 - bit)) & 1);
      }

    return new Pc88File { PixelData = pixelData };
  }
}
