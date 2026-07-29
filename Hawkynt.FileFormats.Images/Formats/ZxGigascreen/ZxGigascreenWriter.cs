using System;

namespace FileFormat.ZxGigascreen;

/// <summary>Assembles ZX Spectrum Gigascreen (.gsc) file bytes from a <see cref="ZxGigascreenFile"/>.</summary>
public static class ZxGigascreenWriter {

  public static byte[] ToBytes(ZxGigascreenFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxGigascreenReader.FileSize];

    _InterleaveBitmap(file.BitmapData1, result, 0);
    file.AttributeData1.AsSpan(0, ZxGigascreenReader.AttributeSize).CopyTo(result.AsSpan(ZxGigascreenReader.BitmapSize));

    _InterleaveBitmap(file.BitmapData2, result, ZxGigascreenReader.ScreenSize);
    file.AttributeData2.AsSpan(0, ZxGigascreenReader.AttributeSize).CopyTo(result.AsSpan(ZxGigascreenReader.ScreenSize + ZxGigascreenReader.BitmapSize));

    return result;
  }

  private static void _InterleaveBitmap(byte[] linear, byte[] result, int baseOffset) {
    for (var y = 0; y < ZxGigascreenReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var dstOffset = baseOffset + third * 2048 + pixelLine * 256 + characterRow * ZxGigascreenReader.BytesPerRow;
      var srcOffset = y * ZxGigascreenReader.BytesPerRow;
      linear.AsSpan(srcOffset, ZxGigascreenReader.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }
  }
}
