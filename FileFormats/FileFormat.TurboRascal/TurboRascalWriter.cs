using System;

namespace FileFormat.TurboRascal;

/// <summary>Assembles Turbo Rascal Syntax Error (.flf) file bytes.</summary>
public static class TurboRascalWriter {

  public static byte[] ToBytes(TurboRascalFile file) {
    var result = new byte[TurboRascalFile.FileSize];

    TurboRascalFile.Signature.CopyTo(result);
    result[TurboRascalFile.ModeOffset] = TurboRascalFile.ChunkyMode;

    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, TurboRascalFile.PixelDataSize))
      .CopyTo(result.AsSpan(TurboRascalFile.PixelDataOffset));

    // Zero here means 256 entries — the field only has one byte to say it in.
    result[TurboRascalFile.ColorCountOffset] = 0;

    var palette = file.Palette ?? [];
    palette.AsSpan(0, Math.Min(palette.Length, TurboRascalFile.ColorCount * 3))
      .CopyTo(result.AsSpan(TurboRascalFile.PaletteOffset));

    return result;
  }
}
