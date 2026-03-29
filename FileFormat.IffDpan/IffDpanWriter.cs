using System;

namespace FileFormat.IffDpan;

/// <summary>Assembles IFF DPAN bytes from an <see cref="IffDpanFile"/>.</summary>
public static class IffDpanWriter {

  public static byte[] ToBytes(IffDpanFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
