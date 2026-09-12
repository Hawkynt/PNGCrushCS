using System;

namespace FileFormat.Vp8L;

internal static class Vp8LWriter {

  public static byte[] ToBytes(Vp8LFile file) {
    ArgumentNullException.ThrowIfNull(file.Bitstream);
    return file.Bitstream[..];
  }
}
