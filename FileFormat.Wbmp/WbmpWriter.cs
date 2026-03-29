using System;
using System.IO;

namespace FileFormat.Wbmp;

/// <summary>Assembles WBMP file bytes from pixel data.</summary>
public static class WbmpWriter {

  public static byte[] ToBytes(WbmpFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    using var ms = new MemoryStream();

    // Type byte (0 = Type 0 WBMP)
    ms.WriteByte(0);

    // FixedHeader byte (reserved, 0)
    ms.WriteByte(0);

    // Width as multi-byte integer
    var widthBytes = WbmpMultiByteInt.Encode(width);
    ms.Write(widthBytes, 0, widthBytes.Length);

    // Height as multi-byte integer
    var heightBytes = WbmpMultiByteInt.Encode(height);
    ms.Write(heightBytes, 0, heightBytes.Length);

    // Pixel data
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var writeLen = Math.Min(expectedPixelBytes, pixelData.Length);
    ms.Write(pixelData, 0, writeLen);

    // Pad with zeros if pixel data is short
    for (var i = writeLen; i < expectedPixelBytes; ++i)
      ms.WriteByte(0);

    return ms.ToArray();
  }
}
