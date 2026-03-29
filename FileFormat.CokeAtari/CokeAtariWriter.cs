using System;

namespace FileFormat.CokeAtari;

/// <summary>Assembles COKE Atari Falcon 16-bit true color file bytes from pixel data.</summary>
public static class CokeAtariWriter {

  public static byte[] ToBytes(CokeAtariFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 2;
    var result = new byte[CokeAtariHeader.StructSize + expectedPixelBytes];

    var header = new CokeAtariHeader((ushort)width, (ushort)height);
    header.WriteTo(result.AsSpan());

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(CokeAtariHeader.StructSize));

    return result;
  }
}
