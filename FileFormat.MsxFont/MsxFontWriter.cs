using System;

namespace FileFormat.MsxFont;

/// <summary>Assembles MSX font pattern table bytes from an <see cref="MsxFontFile"/>.</summary>
public static class MsxFontWriter {

  public static byte[] ToBytes(MsxFontFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MsxFontFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, MsxFontFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
