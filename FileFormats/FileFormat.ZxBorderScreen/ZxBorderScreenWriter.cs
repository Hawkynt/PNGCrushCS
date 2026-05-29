using System;

namespace FileFormat.ZxBorderScreen;

/// <summary>Assembles ZX Spectrum Border Screen (.bsc) file bytes from a <see cref="ZxBorderScreenFile"/>.</summary>
public static class ZxBorderScreenWriter {

  public static byte[] ToBytes(ZxBorderScreenFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxBorderScreenReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxBorderScreenReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxBorderScreenReader.BytesPerRow;
      var srcOffset = y * ZxBorderScreenReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxBorderScreenReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy attribute data after bitmap
    file.AttributeData.AsSpan(0, ZxBorderScreenReader.AttributeSize).CopyTo(result.AsSpan(ZxBorderScreenReader.BitmapSize));

    // Copy border data after attributes
    file.BorderData.AsSpan(0, ZxBorderScreenReader.BorderSize).CopyTo(result.AsSpan(ZxBorderScreenReader.BitmapSize + ZxBorderScreenReader.AttributeSize));

    return result;
  }
}
