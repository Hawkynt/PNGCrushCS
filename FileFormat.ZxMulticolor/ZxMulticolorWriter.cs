using System;

namespace FileFormat.ZxMulticolor;

/// <summary>Assembles ZX Spectrum Multicolor (.mlt) file bytes from a <see cref="ZxMulticolorFile"/>.</summary>
public static class ZxMulticolorWriter {

  public static byte[] ToBytes(ZxMulticolorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxMulticolorReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxMulticolorReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxMulticolorReader.BytesPerRow;
      var srcOffset = y * ZxMulticolorReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxMulticolorReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy per-scanline attribute data directly after bitmap
    file.AttributeData.AsSpan(0, ZxMulticolorReader.AttributeSize).CopyTo(result.AsSpan(ZxMulticolorReader.BitmapSize));

    return result;
  }
}
