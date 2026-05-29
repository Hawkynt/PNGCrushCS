using System;

namespace FileFormat.CpcPlus;

/// <summary>Assembles CPC Plus Mode 1 image bytes from a <see cref="CpcPlusFile"/>.</summary>
public static class CpcPlusWriter {

  public static byte[] ToBytes(CpcPlusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CpcPlusFile.ExpectedFileSize];

    // Interleave: convert linear row order back to CPC memory layout
    var lineCount = Math.Min(CpcPlusFile.PixelHeight, file.PixelData.Length / CpcPlusFile.BytesPerRow);
    for (var y = 0; y < lineCount; ++y) {
      var srcOffset = y * CpcPlusFile.BytesPerRow;
      var dstOffset = (y / 8) * CpcPlusFile.BytesPerRow + (y % 8) * 2048;
      file.PixelData.AsSpan(srcOffset, CpcPlusFile.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    // Write palette data after screen data
    file.PaletteData.AsSpan(0, Math.Min(file.PaletteData.Length, CpcPlusFile.PaletteDataSize)).CopyTo(result.AsSpan(CpcPlusFile.ScreenDataSize));

    return result;
  }
}
