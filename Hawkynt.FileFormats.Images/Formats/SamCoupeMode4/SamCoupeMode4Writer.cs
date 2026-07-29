using System;

namespace FileFormat.SamCoupeMode4;

/// <summary>Assembles SAM Coupe mode 4 screen bytes.</summary>
public static class SamCoupeMode4Writer {

  public static byte[] ToBytes(SamCoupeMode4File file) {
    var result = new byte[SamCoupeMode4File.FileSize];

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, SamCoupeMode4File.BitmapDataSize)).CopyTo(result);

    var palette = file.Palette ?? [];
    palette.AsSpan(0, Math.Min(palette.Length, SamCoupePalette.EntryCount))
      .CopyTo(result.AsSpan(SamCoupeMode4File.PaletteOffset));

    // No palette changes part-way down the screen: close the interrupt block immediately.
    result[SamCoupeMode4File.InterruptOffset] = SamCoupeMode4File.InterruptTerminator;

    return result;
  }
}
