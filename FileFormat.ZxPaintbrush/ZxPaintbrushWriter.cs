using System;

namespace FileFormat.ZxPaintbrush;

/// <summary>Assembles ZX-Paintbrush file bytes from a <see cref="ZxPaintbrushFile"/>.</summary>
public static class ZxPaintbrushWriter {

  public static byte[] ToBytes(ZxPaintbrushFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var extraLength = file.ExtraData?.Length ?? 0;
    var result = new byte[ZxPaintbrushReader.MinFileSize + extraLength];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxPaintbrushReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxPaintbrushReader.BytesPerRow;
      var srcOffset = y * ZxPaintbrushReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxPaintbrushReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy attribute data directly after bitmap
    file.AttributeData.AsSpan(0, ZxPaintbrushReader.AttributeSize).CopyTo(result.AsSpan(ZxPaintbrushReader.BitmapSize));

    // Copy any extra data after the standard screen
    if (extraLength > 0)
      file.ExtraData.AsSpan(0, extraLength).CopyTo(result.AsSpan(ZxPaintbrushReader.MinFileSize));

    return result;
  }
}
