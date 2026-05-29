using System;

namespace FileFormat.Sprite64;

/// <summary>Assembles C64 sprite file bytes from a Sprite64File.</summary>
public static class Sprite64Writer {

  public static byte[] ToBytes(Sprite64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Sprite64File.ExpectedFileSize];

    // Sprite pixel data (63 bytes)
    var copyLen = Math.Min(file.SpriteData.Length, Sprite64File.SpriteDataSize);
    file.SpriteData.AsSpan(0, copyLen).CopyTo(result.AsSpan(0));

    // Mode byte (byte 63)
    result[Sprite64File.SpriteDataSize] = file.ModeByte;

    return result;
  }
}
