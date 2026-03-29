using System;

namespace FileFormat.ZxUlaPlus;

/// <summary>Assembles ZX Spectrum ULAplus (.ulp) file bytes from a <see cref="ZxUlaPlusFile"/>.</summary>
public static class ZxUlaPlusWriter {

  public static byte[] ToBytes(ZxUlaPlusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxUlaPlusReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxUlaPlusReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxUlaPlusReader.BytesPerRow;
      var srcOffset = y * ZxUlaPlusReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxUlaPlusReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    file.AttributeData.AsSpan(0, ZxUlaPlusReader.AttributeSize).CopyTo(result.AsSpan(ZxUlaPlusReader.BitmapSize));
    file.PaletteData.AsSpan(0, ZxUlaPlusReader.PaletteSize).CopyTo(result.AsSpan(ZxUlaPlusReader.BitmapSize + ZxUlaPlusReader.AttributeSize));

    return result;
  }
}
