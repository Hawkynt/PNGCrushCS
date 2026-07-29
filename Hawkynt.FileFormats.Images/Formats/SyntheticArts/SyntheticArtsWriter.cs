using System;

namespace FileFormat.SyntheticArts;

/// <summary>Assembles Synthetic Arts (.srt) file bytes from an in-memory representation.</summary>
public static class SyntheticArtsWriter {

  public static byte[] ToBytes(SyntheticArtsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SyntheticArtsFile.FileSize];

    // Bitmap first; the "JHSy" tag, a 00 01 version field and the palette all follow it.
    file.PixelData.AsSpan(0, Math.Min(SyntheticArtsFile.PixelDataSize, file.PixelData.Length)).CopyTo(result);
    SyntheticArtsFile.Tag.CopyTo(result.AsSpan(SyntheticArtsFile.TagOffset));
    result[SyntheticArtsFile.TagOffset + 4] = 0x00;
    result[SyntheticArtsFile.TagOffset + 5] = 0x01;
    new SyntheticArtsHeader(file.Palette).WriteTo(result.AsSpan(SyntheticArtsFile.PaletteOffset));

    return result;
  }
}
