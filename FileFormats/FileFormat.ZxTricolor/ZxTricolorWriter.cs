using System;

namespace FileFormat.ZxTricolor;

/// <summary>Assembles ZX Spectrum Tricolor (.3cl) file bytes from a <see cref="ZxTricolorFile"/>.</summary>
public static class ZxTricolorWriter {

  public static byte[] ToBytes(ZxTricolorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxTricolorReader.FileSize];

    _InterleaveBitmap(file.BitmapData1, result, 0);
    file.AttributeData1.AsSpan(0, ZxTricolorReader.AttributeSize).CopyTo(result.AsSpan(ZxTricolorReader.BitmapSize));

    _InterleaveBitmap(file.BitmapData2, result, ZxTricolorReader.ScreenSize);
    file.AttributeData2.AsSpan(0, ZxTricolorReader.AttributeSize).CopyTo(result.AsSpan(ZxTricolorReader.ScreenSize + ZxTricolorReader.BitmapSize));

    _InterleaveBitmap(file.BitmapData3, result, ZxTricolorReader.ScreenSize * 2);
    file.AttributeData3.AsSpan(0, ZxTricolorReader.AttributeSize).CopyTo(result.AsSpan(ZxTricolorReader.ScreenSize * 2 + ZxTricolorReader.BitmapSize));

    return result;
  }

  private static void _InterleaveBitmap(byte[] linear, byte[] result, int baseOffset) {
    for (var y = 0; y < ZxTricolorReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = baseOffset + third * 2048 + pixelLine * 256 + characterRow * ZxTricolorReader.BytesPerRow;
      var srcOffset = y * ZxTricolorReader.BytesPerRow;
      linear.AsSpan(srcOffset, ZxTricolorReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }
  }
}
