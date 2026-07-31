using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen4;

/// <summary>Assembles MSX Screen 4 picture bytes from a <see cref="MsxScreen4File"/>.</summary>
public static class MsxScreen4Writer {

  public static byte[] ToBytes(MsxScreen4File file) {
    var data = file.Data ?? [];
    var result = new byte[MsxScreen4File.MinimumFileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    MsxGraphics.WriteBsaveHeader(result, MsxScreen4File.VramSize - 1);

    return result;
  }
}
