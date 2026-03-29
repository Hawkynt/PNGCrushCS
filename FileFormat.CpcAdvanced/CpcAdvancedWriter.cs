using System;

namespace FileFormat.CpcAdvanced;

/// <summary>Assembles CPC Advanced Mode 0 image bytes from a <see cref="CpcAdvancedFile"/>.</summary>
public static class CpcAdvancedWriter {

  public static byte[] ToBytes(CpcAdvancedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Interleave: convert linear row order back to CPC memory layout
    var result = new byte[CpcAdvancedFile.ExpectedFileSize];
    var lineCount = Math.Min(CpcAdvancedFile.PixelHeight, file.PixelData.Length / CpcAdvancedFile.BytesPerRow);

    for (var y = 0; y < lineCount; ++y) {
      var srcOffset = y * CpcAdvancedFile.BytesPerRow;
      var dstOffset = (y / 8) * CpcAdvancedFile.BytesPerRow + (y % 8) * 2048;
      file.PixelData.AsSpan(srcOffset, CpcAdvancedFile.BytesPerRow).CopyTo(result.AsSpan(dstOffset));
    }

    return result;
  }
}
