using System;

namespace FileFormat.NokiaPictureMessage;

/// <summary>Assembles Nokia Picture Message (.npm) file bytes from pixel data.</summary>
public static class NokiaPictureMessageWriter {

  public static byte[] ToBytes(NokiaPictureMessageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var fileSize = NokiaPictureMessageHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new NokiaPictureMessageHeader(0x00, (byte)width, (byte)height, 0x01);
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(NokiaPictureMessageHeader.StructSize));

    return result;
  }
}
