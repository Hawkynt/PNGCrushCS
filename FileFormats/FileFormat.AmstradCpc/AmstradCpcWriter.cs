using System;

namespace FileFormat.AmstradCpc;

/// <summary>Assembles Amstrad CPC screen memory dump bytes from an <see cref="AmstradCpcFile"/>.</summary>
public static class AmstradCpcWriter {

  /// <summary>Standard CPC screen memory size in bytes.</summary>
  private const int _SCREEN_SIZE = 16384;

  /// <summary>Bytes per scanline in CPC memory.</summary>
  private const int _BYTES_PER_LINE = 80;

  /// <summary>Number of scanlines.</summary>
  private const int _HEIGHT = 200;

  public static byte[] ToBytes(AmstradCpcFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Interleave: convert linear row order back to CPC memory layout
    // Line Y address = ((Y / 8) * 80) + ((Y % 8) * 2048)
    var result = new byte[_SCREEN_SIZE];
    var lineCount = Math.Min(_HEIGHT, file.PixelData.Length / _BYTES_PER_LINE);

    for (var y = 0; y < lineCount; ++y) {
      var srcOffset = y * _BYTES_PER_LINE;
      var dstOffset = (y / 8) * _BYTES_PER_LINE + (y % 8) * 2048;
      file.PixelData.AsSpan(srcOffset, _BYTES_PER_LINE).CopyTo(result.AsSpan(dstOffset));
    }

    return result;
  }
}
