using System;
using System.IO;

namespace FileFormat.Thomson;

/// <summary>Parses thomson to7/mo5 binary screen dump from raw bytes.</summary>
public static class ThomsonReader {

  public static ThomsonFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ThomsonFile FromStream(Stream stream) {
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

  public static ThomsonFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ThomsonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ThomsonFile.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected 8000.");

    var pixelData = new byte[ThomsonFile.ImageWidth * ThomsonFile.ImageHeight];
    for (var y = 0; y < ThomsonFile.ImageHeight; ++y)
      for (var x = 0; x < ThomsonFile.ImageWidth; x += 8) {
        var b = data[y * 40 + x / 8];
        for (var bit = 0; bit < 8 && x + bit < ThomsonFile.ImageWidth; ++bit)
          pixelData[y * ThomsonFile.ImageWidth + x + bit] = (byte)((b >> (7 - bit)) & 1);
      }

    return new ThomsonFile { PixelData = pixelData };
  }
}
