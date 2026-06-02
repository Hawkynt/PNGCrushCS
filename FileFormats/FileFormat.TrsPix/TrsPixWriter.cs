using System;

namespace FileFormat.TrsPix;

public static class TrsPixWriter {

  public static byte[] ToBytes(TrsPixFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    if (file.Mode > 3)
      throw new InvalidOperationException($"Unsupported TRS-80 PIX mode {file.Mode}.");

    var buf = new byte[5 + file.PixelData.Length];
    buf[0] = 0x50; // 'P'
    buf[1] = 0x49; // 'I'
    buf[2] = 0x58; // 'X'
    buf[3] = 0x00;
    buf[4] = file.Mode;
    file.PixelData.AsSpan().CopyTo(buf.AsSpan(5));
    return buf;
  }
}
