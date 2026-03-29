using System;

namespace FileFormat.Qrt;

/// <summary>Assembles QRT Ray Tracer file bytes from pixel data.</summary>
public static class QrtWriter {

  public static byte[] ToBytes(QrtFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 3;
    var result = new byte[QrtHeader.StructSize + expectedPixelBytes];

    var header = new QrtHeader((ushort)width, (ushort)height);
    header.WriteTo(result.AsSpan());

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(QrtHeader.StructSize));

    return result;
  }
}
