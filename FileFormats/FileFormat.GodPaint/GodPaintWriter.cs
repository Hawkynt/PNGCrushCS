using System;

namespace FileFormat.GodPaint;

/// <summary>Assembles GodPaint screen dump bytes from pixel data.</summary>
public static class GodPaintWriter {

  /// <summary>The exact file size of a valid GodPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = GodPaintFile.ExpectedFileSize;

  public static byte[] ToBytes(GodPaintFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];

    // Two reserved bytes, then the dimensions big-endian; RGB565 pixels follow.
    result[GodPaintFile.DimensionsOffset] = 320 >> 8;
    result[GodPaintFile.DimensionsOffset + 1] = 320 & 0xFF;
    result[GodPaintFile.DimensionsOffset + 2] = 240 >> 8;
    result[GodPaintFile.DimensionsOffset + 3] = 240 & 0xFF;

    pixelData.AsSpan(0, Math.Min(pixelData.Length, GodPaintFile.PixelDataSize))
      .CopyTo(result.AsSpan(GodPaintFile.HeaderSize));

    return result;
  }
}
