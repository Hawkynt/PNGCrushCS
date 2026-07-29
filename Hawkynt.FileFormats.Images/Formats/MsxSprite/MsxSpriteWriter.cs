using System;

namespace FileFormat.MsxSprite;

/// <summary>Assembles MSX sprite pattern table bytes from an <see cref="MsxSpriteFile"/>.</summary>
public static class MsxSpriteWriter {

  public static byte[] ToBytes(MsxSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MsxSpriteFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, MsxSpriteFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
