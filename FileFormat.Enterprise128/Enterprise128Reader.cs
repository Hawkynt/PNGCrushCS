using System;
using System.IO;

namespace FileFormat.Enterprise128;

/// <summary>Parses enterprise 128/elan screen dump from raw bytes.</summary>
public static class Enterprise128Reader {

  public static Enterprise128File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Enterprise128File FromStream(Stream stream) {
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

  public static Enterprise128File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Enterprise128File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Enterprise128File.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 16384.");

    var pixelData = new byte[Enterprise128File.ImageWidth * Enterprise128File.ImageHeight];
    for (var y = 0; y < Enterprise128File.ImageHeight; ++y)
      for (var x = 0; x < Enterprise128File.ImageWidth; x += 8) {
        var b = data[y * 64 + x / 8];
        for (var bit = 0; bit < 8 && x + bit < Enterprise128File.ImageWidth; ++bit)
          pixelData[y * Enterprise128File.ImageWidth + x + bit] = (byte)((b >> (7 - bit)) & 1);
      }

    return new Enterprise128File { PixelData = pixelData };
  }
}
