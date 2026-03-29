using System;

namespace FileFormat.Hrz;

/// <summary>Assembles HRZ file bytes from pixel data.</summary>
public static class HrzWriter {

  /// <summary>The exact file size of a valid HRZ image (256 x 240 x 3 bytes).</summary>
  private const int _EXPECTED_SIZE = 256 * 240 * 3;

  public static byte[] ToBytes(HrzFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
