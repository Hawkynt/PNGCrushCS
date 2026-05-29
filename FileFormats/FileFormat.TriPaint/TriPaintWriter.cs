using System;

namespace FileFormat.TriPaint;

/// <summary>Assembles TriPaint screen dump bytes from pixel data.</summary>
public static class TriPaintWriter {

  /// <summary>The exact file size of a valid TriPaint screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = TriPaintFile.ExpectedFileSize;

  public static byte[] ToBytes(TriPaintFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
