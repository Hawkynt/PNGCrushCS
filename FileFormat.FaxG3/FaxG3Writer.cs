using System;

namespace FileFormat.FaxG3;

/// <summary>Assembles Raw Group 3 fax image file bytes.</summary>
public static class FaxG3Writer {

  public static byte[] ToBytes(FaxG3File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelBytes = file.PixelData.Length;
    var fileSize = FaxG3File.HeaderSize + pixelBytes;
    var result = new byte[fileSize];

    // Pack Width (uint16 LE) + Height (uint16 LE) + reserved (2 bytes) into the 6-byte header.
    var w = (ushort)file.Width;
    var h = (ushort)file.Height;
    result[0] = (byte)(w & 0xFF);
    result[1] = (byte)((w >> 8) & 0xFF);
    result[2] = (byte)(h & 0xFF);
    result[3] = (byte)((h >> 8) & 0xFF);

    file.PixelData.AsSpan(0, Math.Min(pixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(FaxG3File.HeaderSize));
    return result;
  }
}
