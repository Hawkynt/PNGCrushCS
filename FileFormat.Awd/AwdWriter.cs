using System;

namespace FileFormat.Awd;

/// <summary>Assembles AWD (Microsoft Fax) file bytes from pixel data.</summary>
public static class AwdWriter {

  public static byte[] ToBytes(AwdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var fileSize = AwdHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Write header fields (WriteTo clears gaps first, so magic must come after)
    var header = new AwdHeader(
      Version: 1,
      Width: (uint)width,
      Height: (uint)height,
      Reserved: 0
    );
    header.WriteTo(span);

    // Write magic after WriteTo since it clears the gap at bytes 0-3
    AwdHeader.Magic.CopyTo(span);

    // Write pixel data
    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(AwdHeader.StructSize));

    return result;
  }
}
