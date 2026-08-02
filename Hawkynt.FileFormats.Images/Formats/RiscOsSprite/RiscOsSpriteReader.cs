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

  public static RiscOsSpriteFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < RiscOsSpriteFile.HeaderSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected at least 16.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}");

    var pixelCount = width * height;

    // Stopping at the end of the file and leaving the rest black let anything named .spr through: a
    // sprite archive beginning "\x01\x00 Sprite File" read those bytes as a size and came back as
    // 1 by 21280, a column of pixels the file never held.
    var needed = pixelCount * 2;
    var available = data.Length - RiscOsSpriteFile.HeaderSize;
    if (available < needed)
      throw new InvalidDataException($"A sprite of {width}x{height} needs {needed} bytes of pixel data; this file has {available}.");

    var pixelData = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var offset = RiscOsSpriteFile.HeaderSize + i * 2;
      var rgb555 = (ushort)(data[offset] | (data[offset + 1] << 8));
      var r5 = (rgb555 >> 10) & 0x1F;
      var g5 = (rgb555 >> 5) & 0x1F;
      var b5 = rgb555 & 0x1F;
      pixelData[i * 3] = (byte)((r5 << 3) | (r5 >> 2));
      pixelData[i * 3 + 1] = (byte)((g5 << 3) | (g5 >> 2));
      pixelData[i * 3 + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new RiscOsSpriteFile { Width = width, Height = height, PixelData = pixelData };
    }

  public static RiscOsSpriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
