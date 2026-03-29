using System;

namespace FileFormat.CpcOverscan;

/// <summary>Assembles CPC overscan image bytes from a <see cref="CpcOverscanFile"/>.</summary>
public static class CpcOverscanWriter {

  /// <summary>Size of each interleaved memory bank.</summary>
  private const int _BANK_SIZE = 16384;

  public static byte[] ToBytes(CpcOverscanFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CpcOverscanFile.ExpectedFileSize];
    var linesPerBank = CpcOverscanFile.PixelHeight / 2;

    for (var bank = 0; bank < 2; ++bank) {
      var bankOffset = bank * _BANK_SIZE;
      for (var y = 0; y < linesPerBank; ++y) {
        var srcY = bank * linesPerBank + y;
        if (srcY >= CpcOverscanFile.PixelHeight)
          break;

        var srcOffset = srcY * CpcOverscanFile.BytesPerRow;
        var dstOffset = bankOffset + (y / 8) * CpcOverscanFile.BytesPerRow + (y % 8) * 2048;
        var copyLen = Math.Min(CpcOverscanFile.BytesPerRow, file.PixelData.Length - srcOffset);
        if (copyLen > 0)
          file.PixelData.AsSpan(srcOffset, copyLen).CopyTo(result.AsSpan(dstOffset));
      }
    }

    return result;
  }
}
