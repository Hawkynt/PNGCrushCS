using System;

namespace FileFormat.Rembrandt;

/// <summary>Assembles Atari Falcon Rembrandt file bytes from a RembrandtFile.</summary>
public static class RembrandtWriter {

  public static byte[] ToBytes(RembrandtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedPixelBytes = file.Width * file.Height * 2;
    var result = new byte[RembrandtFile.HeaderSize + expectedPixelBytes];

    // Write dimensions (BE u16)
    result[0] = (byte)((file.Width >> 8) & 0xFF);
    result[1] = (byte)(file.Width & 0xFF);
    result[2] = (byte)((file.Height >> 8) & 0xFF);
    result[3] = (byte)(file.Height & 0xFF);

    // Write pixel data
    var copyLen = Math.Min(file.PixelData.Length, expectedPixelBytes);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(RembrandtFile.HeaderSize));

    return result;
  }
}
