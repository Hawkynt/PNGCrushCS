using System;

namespace FileFormat.CpcSprite;

/// <summary>Assembles CPC sprite data bytes from a <see cref="CpcSpriteFile"/>.</summary>
public static class CpcSpriteWriter {

  public static byte[] ToBytes(CpcSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CpcSpriteFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, CpcSpriteFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
