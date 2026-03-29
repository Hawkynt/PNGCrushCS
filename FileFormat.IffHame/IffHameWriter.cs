using System;

namespace FileFormat.IffHame;

/// <summary>Assembles IFF HAM-E bytes from an <see cref="IffHameFile"/>.</summary>
public static class IffHameWriter {

  public static byte[] ToBytes(IffHameFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
