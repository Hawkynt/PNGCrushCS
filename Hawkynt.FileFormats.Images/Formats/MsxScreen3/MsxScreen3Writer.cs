using System;

namespace FileFormat.MsxScreen3;

/// <summary>Assembles MSX Screen 3 picture bytes from a <see cref="MsxScreen3File"/>.</summary>
public static class MsxScreen3Writer {

  public static byte[] ToBytes(MsxScreen3File file) {
    var data = file.Data ?? [];
    var result = new byte[MsxScreen3File.LongFileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    // The header names the last address the load covers, which is what tells a reader how much of
    // video memory the file is describing.
    FileFormat.Core.MsxGraphics.WriteBsaveHeader(
      result, MsxScreen3File.ScreenMapOffset + MsxScreen3File.ScreenMapSize - 1);

    return result;
  }
}
