using System;

namespace FileFormat.CoCo3;

/// <summary>Assembles CoCo 3 GIME 320x200x16 graphics bytes from a <see cref="CoCo3File"/>.</summary>
public static class CoCo3Writer {

  public static byte[] ToBytes(CoCo3File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CoCo3File.ExpectedFileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, CoCo3File.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
