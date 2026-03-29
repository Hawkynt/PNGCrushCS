using System;

namespace FileFormat.IffAnim8;

/// <summary>Assembles IFF ANIM8 bytes from an <see cref="IffAnim8File"/>.</summary>
public static class IffAnim8Writer {

  public static byte[] ToBytes(IffAnim8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
