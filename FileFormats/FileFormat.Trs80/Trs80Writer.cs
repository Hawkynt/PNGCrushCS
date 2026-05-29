using System;

namespace FileFormat.Trs80;

/// <summary>Assembles TRS-80 hi-res graphics screen dump bytes from a <see cref="Trs80File"/>.</summary>
public static class Trs80Writer {

  public static byte[] ToBytes(Trs80File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Trs80File.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, Trs80File.FileSize)).CopyTo(result);
    return result;
  }
}
