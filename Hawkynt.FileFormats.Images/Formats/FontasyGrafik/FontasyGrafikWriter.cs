using System;

namespace FileFormat.FontasyGrafik;

/// <summary>Assembles Atari ST Fontasy Grafik image bytes from a FontasyGrafikFile.</summary>
public static class FontasyGrafikWriter {

  public static byte[] ToBytes(FontasyGrafikFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FontasyGrafikFile.ExpectedFileSize];

    new FontasyGrafikHeader(file.Palette).WriteTo(result);

    // 2 bytes padding at offset 32 are left as zero
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, FontasyGrafikFile.PlanarDataSize)).CopyTo(result.AsSpan(FontasyGrafikFile.PaletteSize + FontasyGrafikFile.PaddingSize));

    return result;
  }
}
