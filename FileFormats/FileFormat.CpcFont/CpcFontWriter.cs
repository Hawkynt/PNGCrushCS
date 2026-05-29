using System;

namespace FileFormat.CpcFont;

/// <summary>Assembles CPC font file bytes from a <see cref="CpcFontFile"/>.</summary>
public static class CpcFontWriter {

  public static byte[] ToBytes(CpcFontFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CpcFontFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, CpcFontFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
