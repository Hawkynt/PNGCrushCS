using System;

namespace FileFormat.Rembrandt;

/// <summary>Assembles Atari Falcon Rembrandt file bytes from a RembrandtFile.</summary>
public static class RembrandtWriter {

  public static byte[] ToBytes(RembrandtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedPixelBytes = file.Width * file.Height * 2;
    var result = new byte[RembrandtHeader.StructSize + expectedPixelBytes];
    RembrandtHeader.Write(result, file.Width, file.Height);

    var copyLen = Math.Min(file.PixelData.Length, expectedPixelBytes);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(RembrandtHeader.StructSize));

    return result;
  }
}
