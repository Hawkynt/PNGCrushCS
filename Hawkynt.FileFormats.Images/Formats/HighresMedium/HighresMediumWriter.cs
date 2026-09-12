using System;

namespace FileFormat.HighresMedium;

/// <summary>Assembles HighresMedium (.hrm) file bytes from a <see cref="HighresMediumFile"/>.</summary>
public static class HighresMediumWriter {

  public static byte[] ToBytes(HighresMediumFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Palettes);

    var result = new byte[HighresMediumFile.FileSize];

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, HighresMediumFile.BitmapSize)).CopyTo(result);

    var palettes = HighresMediumFile.PaletteRowSize * HighresMediumFile.ImageHeight;
    file.Palettes.AsSpan(0, Math.Min(file.Palettes.Length, palettes))
      .CopyTo(result.AsSpan(HighresMediumFile.PalettesOffset));

    return result;
  }
}
