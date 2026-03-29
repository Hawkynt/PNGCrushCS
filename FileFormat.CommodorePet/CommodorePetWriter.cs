using System;

namespace FileFormat.CommodorePet;

/// <summary>Assembles commodore pet petscii screen dump bytes from pixel data.</summary>
public static class CommodorePetWriter {

  public static byte[] ToBytes(CommodorePetFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var result = new byte[CommodorePetFile.FileSize];

    var len = Math.Min(result.Length, pixelData.Length);
    pixelData.AsSpan(0, len).CopyTo(result.AsSpan(0));

    return result;
  }
}
