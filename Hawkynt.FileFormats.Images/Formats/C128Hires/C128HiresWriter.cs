using System;

namespace FileFormat.C128Hires;

/// <summary>Assembles C128 hires 320x200 mono bitmap bytes from a <see cref="C128HiresFile"/>.</summary>
public static class C128HiresWriter {

  public static byte[] ToBytes(C128HiresFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[C128HiresFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, C128HiresFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
