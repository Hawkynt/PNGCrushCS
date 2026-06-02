using System;
using System.Buffers.Binary;

namespace FileFormat.AtariSif;

public static class AtariSifWriter {

  public static byte[] ToBytes(AtariSifFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    if (file.AnticMode is not (8 or 9 or 15))
      throw new InvalidOperationException($"Unsupported Atari ANTIC mode {file.AnticMode}.");

    var buf = new byte[10 + file.PixelData.Length];
    buf[0] = 0x53; buf[1] = 0x49; buf[2] = 0x46; buf[3] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4, 2), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(6, 2), (ushort)file.Height);
    buf[8] = file.AnticMode;
    buf[9] = 0;
    file.PixelData.AsSpan().CopyTo(buf.AsSpan(10));
    return buf;
  }
}
