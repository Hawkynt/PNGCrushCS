using System;

namespace FileFormat.Atari7800;

/// <summary>Assembles atari 7800 maria screen dump bytes from pixel data.</summary>
public static class Atari7800Writer {

  public static byte[] ToBytes(Atari7800File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var result = new byte[Atari7800File.FileSize];

    var len = Math.Min(result.Length, pixelData.Length);
    pixelData.AsSpan(0, len).CopyTo(result.AsSpan(0));

    return result;
  }
}
