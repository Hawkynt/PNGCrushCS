using System;

namespace FileFormat.AtariFalconXga;

/// <summary>Assembles Atari Falcon XGA 16-bit true color file bytes from pixel data.</summary>
public static class AtariFalconXgaWriter {

  public static byte[] ToBytes(AtariFalconXgaFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    // Nothing precedes the samples, so the file must come out at exactly one of the two lengths the
    // format has; any other length names no size and cannot be read back.
    _ = AtariFalconXgaFile.SizeOf(width * height * 2);

    var result = new byte[width * height * 2];
    pixelData.AsSpan(0, Math.Min(result.Length, pixelData.Length)).CopyTo(result);
    return result;
  }
}
