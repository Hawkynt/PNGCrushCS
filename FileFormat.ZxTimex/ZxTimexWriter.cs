using System;

namespace FileFormat.ZxTimex;

/// <summary>Assembles Timex HiColor (.tmx) file bytes from a <see cref="ZxTimexFile"/>.</summary>
public static class ZxTimexWriter {

  public static byte[] ToBytes(ZxTimexFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxTimexReader.FileSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxTimexReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxTimexReader.BytesPerRow;
      var srcOffset = y * ZxTimexReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxTimexReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Copy extended attribute data directly after bitmap
    file.AttributeData.AsSpan(0, ZxTimexReader.AttributeSize).CopyTo(result.AsSpan(ZxTimexReader.BitmapSize));

    return result;
  }
}
