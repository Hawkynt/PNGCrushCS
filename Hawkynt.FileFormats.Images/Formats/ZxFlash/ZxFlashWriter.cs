using System;

namespace FileFormat.ZxFlash;

/// <summary>Assembles ZX Spectrum Flash animation (.zfl) file bytes from a <see cref="ZxFlashFile"/> (writes first frame only).</summary>
public static class ZxFlashWriter {

  public static byte[] ToBytes(ZxFlashFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxFlashReader.ScreenSize];

    // Interleave linear bitmap data back to ZX Spectrum memory layout
    for (var y = 0; y < ZxFlashReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = third * 2048 + pixelLine * 256 + characterRow * ZxFlashReader.BytesPerRow;
      var srcOffset = y * ZxFlashReader.BytesPerRow;
      file.BitmapData.AsSpan(srcOffset, ZxFlashReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    file.AttributeData.AsSpan(0, ZxFlashReader.AttributeSize).CopyTo(result.AsSpan(ZxFlashReader.BitmapSize));

    return result;
  }
}
