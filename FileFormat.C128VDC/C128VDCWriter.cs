using System;

namespace FileFormat.C128VDC;

/// <summary>Assembles C128 VDC 640x200 mono bitmap bytes from a <see cref="C128VDCFile"/>.</summary>
public static class C128VDCWriter {

  public static byte[] ToBytes(C128VDCFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[C128VDCFile.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, C128VDCFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
