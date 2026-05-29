using System;

namespace FileFormat.SpeederFalcon;

/// <summary>Assembles Speeder Falcon screen dump bytes from pixel data.</summary>
public static class SpeederFalconWriter {

  /// <summary>The exact file size of a valid Speeder Falcon screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = SpeederFalconFile.ExpectedFileSize;

  public static byte[] ToBytes(SpeederFalconFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
