using System;

namespace FileFormat.Olpc565;

/// <summary>Assembles OLPC RGB565 (.565) file bytes from pixel data.</summary>
public static class Olpc565Writer {

  public static byte[] ToBytes(Olpc565File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 2;
    var fileSize = Olpc565Header.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new Olpc565Header((ushort)width, (ushort)height);
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(Olpc565Header.StructSize));

    return result;
  }
}
