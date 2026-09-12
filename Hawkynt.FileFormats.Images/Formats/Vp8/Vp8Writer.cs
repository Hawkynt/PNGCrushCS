using System;

namespace FileFormat.Vp8;

internal static class Vp8Writer {

  public static byte[] ToBytes(Vp8File file) {
    ArgumentNullException.ThrowIfNull(file.Bitstream);
    return file.Bitstream[..];
  }
}
