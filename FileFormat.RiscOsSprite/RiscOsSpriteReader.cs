using System;
using System.IO;

namespace FileFormat.RiscOsSprite;

/// <summary>Parses acorn risc os sprite format from raw bytes.</summary>
public static class RiscOsSpriteReader {

  public static RiscOsSpriteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RiscOsSpriteFile FromStream(Stream stream) {
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

  public static RiscOsSpriteFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static RiscOsSpriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RiscOsSpriteFile.HeaderSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected at least 16.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}");

    var pixelCount = width * height;
    var pixelData = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount && RiscOsSpriteFile.HeaderSize + i * 2 + 1 < data.Length; ++i) {
      var offset = RiscOsSpriteFile.HeaderSize + i * 2;
      var rgb555 = (ushort)(data[offset] | (data[offset + 1] << 8));
      pixelData[i * 3] = (byte)(((rgb555 >> 10) & 0x1F) << 3);
      pixelData[i * 3 + 1] = (byte)(((rgb555 >> 5) & 0x1F) << 3);
      pixelData[i * 3 + 2] = (byte)((rgb555 & 0x1F) << 3);
    }

    return new RiscOsSpriteFile { Width = width, Height = height, PixelData = pixelData };
  }
}
