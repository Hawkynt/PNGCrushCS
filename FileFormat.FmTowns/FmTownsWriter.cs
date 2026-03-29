using System;

namespace FileFormat.FmTowns;

/// <summary>Assembles fujitsu fm towns 256-color screen dump bytes from pixel data.</summary>
public static class FmTownsWriter {

  public static byte[] ToBytes(FmTownsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var result = new byte[FmTownsFile.FileSize];

    var len = Math.Min(result.Length, pixelData.Length);
    pixelData.AsSpan(0, len).CopyTo(result.AsSpan(0));

    return result;
  }
}
