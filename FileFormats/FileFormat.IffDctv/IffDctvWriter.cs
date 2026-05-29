using System;

namespace FileFormat.IffDctv;

/// <summary>Assembles IFF DCTV (Composite Video) bytes from an <see cref="IffDctvFile"/>.</summary>
public static class IffDctvWriter {

  public static byte[] ToBytes(IffDctvFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
