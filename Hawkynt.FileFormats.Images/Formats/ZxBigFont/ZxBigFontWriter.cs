using System;

namespace FileFormat.ZxBigFont;

/// <summary>Assembles a ZX big font from a <see cref="ZxBigFontFile"/>.</summary>
public static class ZxBigFontWriter {

  public static byte[] ToBytes(ZxBigFontFile file) {
    var data = file.Data ?? [];
    var result = new byte[data.Length];
    data.AsSpan().CopyTo(result);

    return result;
  }
}
