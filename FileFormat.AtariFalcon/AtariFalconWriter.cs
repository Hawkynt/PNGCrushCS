using System;

namespace FileFormat.AtariFalcon;

/// <summary>Assembles Atari Falcon true-color screen dump bytes from pixel data.</summary>
public static class AtariFalconWriter {

  /// <summary>The exact file size of a valid Atari Falcon screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = AtariFalconFile.ExpectedFileSize;

  public static byte[] ToBytes(AtariFalconFile file) => Assemble(file.PixelData);

  internal static byte[] Assemble(byte[] pixelData) {
    var result = new byte[_EXPECTED_SIZE];
    pixelData.AsSpan(0, Math.Min(pixelData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
