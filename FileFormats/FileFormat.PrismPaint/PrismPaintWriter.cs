using System;

namespace FileFormat.PrismPaint;

/// <summary>Assembles Atari Falcon Prism Paint file bytes from a PrismPaintFile.</summary>
public static class PrismPaintWriter {

  public static byte[] ToBytes(PrismPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedPixelBytes = file.Width * file.Height;
    var result = new byte[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + expectedPixelBytes];

    // Write dimensions (LE u16)
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)((file.Height >> 8) & 0xFF);

    // Write Falcon palette
    PrismPaintFile.ConvertRgbPaletteToFalcon(
      file.Palette.AsSpan(0, Math.Min(file.Palette.Length, PrismPaintFile.PaletteEntryCount * 3)),
      result.AsSpan(PrismPaintFile.HeaderSize, PrismPaintFile.PaletteDataSize)
    );

    // Write pixel data
    var pixelOffset = PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize;
    var copyLen = Math.Min(file.PixelData.Length, expectedPixelBytes);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(pixelOffset));

    return result;
  }
}
