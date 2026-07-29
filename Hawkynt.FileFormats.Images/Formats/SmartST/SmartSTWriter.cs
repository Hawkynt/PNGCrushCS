using System;

namespace FileFormat.SmartST;

/// <summary>Assembles Smart ST screen dump bytes from pixel data.</summary>
public static class SmartSTWriter {

  /// <summary>The exact file size of a valid Smart ST screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = SmartSTFile.ExpectedFileSize;

  public static byte[] ToBytes(SmartSTFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
