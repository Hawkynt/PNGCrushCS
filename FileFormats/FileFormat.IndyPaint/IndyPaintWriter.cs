using System;

namespace FileFormat.IndyPaint;

/// <summary>Assembles IndyPaint screen dump bytes from pixel data.</summary>
public static class IndyPaintWriter {

  /// <summary>The exact file size of a valid IndyPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = IndyPaintFile.ExpectedFileSize;

  public static byte[] ToBytes(IndyPaintFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
