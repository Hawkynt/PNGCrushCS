using System;
using System.Buffers.Binary;

namespace FileFormat.FontasyGrafik;

/// <summary>Assembles Atari ST Fontasy Grafik image bytes from a FontasyGrafikFile.</summary>
public static class FontasyGrafikWriter {

  public static byte[] ToBytes(FontasyGrafikFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FontasyGrafikFile.ExpectedFileSize];
    var span = result.AsSpan();

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span[(i * 2)..], i < file.Palette.Length ? file.Palette[i] : (short)0);

    // 2 bytes padding at offset 32 are left as zero
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, FontasyGrafikFile.PlanarDataSize)).CopyTo(result.AsSpan(FontasyGrafikFile.PaletteSize + FontasyGrafikFile.PaddingSize));

    return result;
  }
}
