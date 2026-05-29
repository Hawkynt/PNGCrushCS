using System;

namespace FileFormat.Rgf;

/// <summary>Assembles RGF (LEGO Mindstorms EV3) file bytes from pixel data.</summary>
public static class RgfWriter {

  /// <summary>Header size: 1 byte width + 1 byte height.</summary>
  private const int _HEADER_SIZE = 2;

  public static byte[] ToBytes(RgfFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var fileSize = _HEADER_SIZE + expectedPixelBytes;
    var result = new byte[fileSize];

    result[0] = (byte)width;
    result[1] = (byte)height;

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(_HEADER_SIZE));

    return result;
  }
}
