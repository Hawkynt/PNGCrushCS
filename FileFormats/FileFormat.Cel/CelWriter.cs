using System;

namespace FileFormat.Cel;

/// <summary>Assembles KiSS CEL file bytes from pixel data.</summary>
public static class CelWriter {

  public static byte[] ToBytes(CelFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.BitsPerPixel, file.XOffset, file.YOffset);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int bitsPerPixel, int xOffset, int yOffset) {
    var mark = bitsPerPixel == 32 ? (byte)0x20 : (byte)0x04;
    var fileSize = CelHeader.StructSize + pixelData.Length;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new CelHeader(
      CelHeader.ExpectedMagic,
      mark,
      (byte)bitsPerPixel,
      (uint)width,
      (uint)height,
      (uint)xOffset,
      (uint)yOffset
    );
    header.WriteTo(span);

    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(CelHeader.StructSize));

    return result;
  }
}
