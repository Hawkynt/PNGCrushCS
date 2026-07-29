using System;

namespace FileFormat.IndyPaint;

/// <summary>Assembles IndyPaint screen dump bytes from pixel data.</summary>
public static class IndyPaintWriter {

  /// <summary>The exact file size of a valid IndyPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = IndyPaintFile.ExpectedFileSize;

  public static byte[] ToBytes(IndyPaintFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];

    // "Indy" signature, then the dimensions big-endian; RGB565 pixels start at the header size.
    IndyPaintFile.Signature.CopyTo(result);
    result[IndyPaintFile.DimensionsOffset] = 320 >> 8;
    result[IndyPaintFile.DimensionsOffset + 1] = 320 & 0xFF;
    result[IndyPaintFile.DimensionsOffset + 2] = 240 >> 8;
    result[IndyPaintFile.DimensionsOffset + 3] = 240 & 0xFF;

    pixelData.AsSpan(0, Math.Min(pixelData.Length, IndyPaintFile.PixelDataSize))
      .CopyTo(result.AsSpan(IndyPaintFile.HeaderSize));

    return result;
  }
}
