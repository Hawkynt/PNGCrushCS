using System;

namespace FileFormat.ZxChrd;

/// <summary>Assembles ZX Spectrum character set (.chr) file bytes from a <see cref="ZxChrdFile"/>.</summary>
public static class ZxChrdWriter {

  public static byte[] ToBytes(ZxChrdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxChrdReader.FileSize];
    file.CharacterData.AsSpan(0, Math.Min(file.CharacterData.Length, ZxChrdReader.FileSize)).CopyTo(result.AsSpan(0));

    return result;
  }
}
