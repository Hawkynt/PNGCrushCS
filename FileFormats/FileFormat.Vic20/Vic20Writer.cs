using System;

namespace FileFormat.Vic20;

/// <summary>Assembles Commodore VIC-20 screen dump file bytes.</summary>
public static class Vic20Writer {

  public static byte[] ToBytes(Vic20File file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[Vic20File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, Vic20File.FileSize)).CopyTo(result);
    return result;
  }
}
