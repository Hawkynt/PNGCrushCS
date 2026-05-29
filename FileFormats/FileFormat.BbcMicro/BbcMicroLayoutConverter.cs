using System;

namespace FileFormat.BbcMicro;

/// <summary>Converts between BBC Micro character-block memory layout and linear scanline layout.</summary>
internal static class BbcMicroLayoutConverter {

  /// <summary>
  /// Converts character-block-ordered screen memory to linear scanline order.
  /// In BBC Micro memory, pixels are stored in 8-byte character blocks arranged
  /// left-to-right, top-to-bottom. Within each block, 8 consecutive bytes represent
  /// the 8 pixel rows of that character cell.
  /// </summary>
  /// <param name="blockData">Raw screen memory in character-block order.</param>
  /// <param name="width">Width in pixels (not used directly; charCols is derived from mode).</param>
  /// <param name="height">Height in pixels.</param>
  /// <param name="mode">Screen mode.</param>
  /// <returns>Pixel data in linear scanline order (top-to-bottom, left-to-right).</returns>
  public static byte[] CharacterBlockToLinear(byte[] blockData, int width, int height, BbcMicroMode mode) {
    var charCols = BbcMicroFile.GetCharacterColumns(mode);
    var charRowCount = height / BbcMicroFile.CharacterRows;
    var bytesPerScanline = charCols;
    var linearData = new byte[height * bytesPerScanline];

    for (var charRow = 0; charRow < charRowCount; ++charRow)
      for (var charCol = 0; charCol < charCols; ++charCol) {
        var blockOffset = (charRow * charCols + charCol) * BbcMicroFile.BytesPerCharacter;
        for (var pixelRow = 0; pixelRow < BbcMicroFile.CharacterRows; ++pixelRow) {
          var scanlineY = charRow * BbcMicroFile.CharacterRows + pixelRow;
          var linearOffset = scanlineY * bytesPerScanline + charCol;
          if (blockOffset + pixelRow < blockData.Length)
            linearData[linearOffset] = blockData[blockOffset + pixelRow];
        }
      }

    return linearData;
  }

  /// <summary>
  /// Converts linear scanline-ordered pixel data to BBC Micro character-block memory layout.
  /// </summary>
  /// <param name="linearData">Pixel data in linear scanline order.</param>
  /// <param name="width">Width in pixels (not used directly; charCols is derived from mode).</param>
  /// <param name="height">Height in pixels.</param>
  /// <param name="mode">Screen mode.</param>
  /// <returns>Raw screen memory in character-block order.</returns>
  public static byte[] LinearToCharacterBlock(byte[] linearData, int width, int height, BbcMicroMode mode) {
    var charCols = BbcMicroFile.GetCharacterColumns(mode);
    var charRowCount = height / BbcMicroFile.CharacterRows;
    var bytesPerScanline = charCols;
    var screenSize = BbcMicroFile.GetExpectedScreenSize(mode);
    var blockData = new byte[screenSize];

    for (var charRow = 0; charRow < charRowCount; ++charRow)
      for (var charCol = 0; charCol < charCols; ++charCol) {
        var blockOffset = (charRow * charCols + charCol) * BbcMicroFile.BytesPerCharacter;
        for (var pixelRow = 0; pixelRow < BbcMicroFile.CharacterRows; ++pixelRow) {
          var scanlineY = charRow * BbcMicroFile.CharacterRows + pixelRow;
          var linearOffset = scanlineY * bytesPerScanline + charCol;
          if (linearOffset < linearData.Length)
            blockData[blockOffset + pixelRow] = linearData[linearOffset];
        }
      }

    return blockData;
  }
}
