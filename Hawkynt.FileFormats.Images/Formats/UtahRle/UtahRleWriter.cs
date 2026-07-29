using System;
using System.IO;

namespace FileFormat.UtahRle;

/// <summary>Assembles Utah RLE file bytes from pixel data.</summary>
public static class UtahRleWriter {

  public static byte[] ToBytes(UtahRleFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Write 14-byte header
    byte flags = 0x02; // no background by default
    if (file.BackgroundColor != null)
      flags = 0x00; // has background

    var header = new UtahRleHeader(
      UtahRleHeader.MagicValue,
      (short)file.XPos,
      (short)file.YPos,
      (short)file.Width,
      (short)file.Height,
      flags,
      (byte)file.NumChannels,
      8,
      0
    );

    Span<byte> headerBytes = stackalloc byte[UtahRleHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write background color if present
    if (file.BackgroundColor != null)
      ms.Write(file.BackgroundColor);

    // Encode and write scanline data
    var encoded = UtahRleEncoder.Encode(file.PixelData, file.Width, file.Height, file.NumChannels);
    ms.Write(encoded);

    return ms.ToArray();
  }
}
