using System;

namespace FileFormat.Cmu;

/// <summary>Assembles CMU Window Manager Bitmap file bytes from pixel data.</summary>
public static class CmuWriter {

  public static byte[] ToBytes(CmuFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var fileSize = CmuHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new CmuHeader(width, height);
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(CmuHeader.StructSize));

    return result;
  }
}
