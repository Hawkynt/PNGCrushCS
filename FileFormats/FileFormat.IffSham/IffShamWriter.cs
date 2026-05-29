using System;

namespace FileFormat.IffSham;

/// <summary>Assembles IFF SHAM (Sliced HAM) bytes from an <see cref="IffShamFile"/>.</summary>
public static class IffShamWriter {

  public static byte[] ToBytes(IffShamFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
