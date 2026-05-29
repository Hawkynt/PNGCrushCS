using System;

namespace FileFormat.Oric;

/// <summary>Assembles Oric hi-res screen dump bytes from screen data.</summary>
public static class OricWriter {

  /// <summary>The exact file size of a valid Oric hi-res screen dump (40 bytes/row x 200 rows).</summary>
  private const int _EXPECTED_SIZE = 40 * 200;

  public static byte[] ToBytes(OricFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.ScreenData);
  }

  internal static byte[] Assemble(byte[] screenData) {
    var result = new byte[_EXPECTED_SIZE];
    screenData.AsSpan(0, Math.Min(screenData.Length, _EXPECTED_SIZE)).CopyTo(result);
    return result;
  }
}
