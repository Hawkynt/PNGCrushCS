using System;

namespace FileFormat.ZxBorderMulticolor;

/// <summary>Assembles ZX Spectrum Border Multicolor 8x4 (.bmc4) file bytes from a <see cref="ZxBorderMulticolorFile"/>.</summary>
public static class ZxBorderMulticolorWriter {

  public static byte[] ToBytes(ZxBorderMulticolorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxBorderMulticolorReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxBorderMulticolorReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxBorderMulticolorReader.BytesPerRow;
      var srcOffset = y * ZxBorderMulticolorReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxBorderMulticolorReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy attribute data after bitmap
    file.AttributeData.AsSpan(0, ZxBorderMulticolorReader.AttributeSize).CopyTo(result.AsSpan(ZxBorderMulticolorReader.BitmapSize));

    // Copy border data after attributes
    file.BorderData.AsSpan(0, ZxBorderMulticolorReader.BorderSize).CopyTo(result.AsSpan(ZxBorderMulticolorReader.BitmapSize + ZxBorderMulticolorReader.AttributeSize));

    return result;
  }
}
