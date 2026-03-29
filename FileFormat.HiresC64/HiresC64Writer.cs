using System;

namespace FileFormat.HiresC64;

/// <summary>Assembles Commodore 64 bare hires bitmap file bytes from a HiresC64File.</summary>
public static class HiresC64Writer {

  public static byte[] ToBytes(HiresC64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiresC64File.ExpectedFileSize];
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, HiresC64File.ExpectedFileSize)).CopyTo(result.AsSpan(0));

    return result;
  }
}
