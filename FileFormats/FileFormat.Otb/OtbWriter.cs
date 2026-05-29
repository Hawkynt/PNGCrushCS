using System;

namespace FileFormat.Otb;

/// <summary>Assembles OTB (Nokia Over-The-Air Bitmap) file bytes from pixel data.</summary>
public static class OtbWriter {

  public static byte[] ToBytes(OtbFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var fileSize = OtbHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new OtbHeader(0x00, (byte)width, (byte)height, 0x01);
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(OtbHeader.StructSize));

    return result;
  }
}
