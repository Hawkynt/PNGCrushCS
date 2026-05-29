using System;

namespace FileFormat.Dds;

/// <summary>Utility for BCn block size calculations.</summary>
internal static class DdsBlockInfo {

  public static int GetBlockSize(DdsFormat format) => format switch {
    DdsFormat.Dxt1 => 8,
    DdsFormat.Dxt3 => 16,
    DdsFormat.Dxt5 => 16,
    DdsFormat.Bc4 => 8,
    DdsFormat.Bc5 => 16,
    DdsFormat.Bc6HUnsigned => 16,
    DdsFormat.Bc6HSigned => 16,
    DdsFormat.Bc7 => 16,
    _ => 0
  };

  public static int CalculateMipSize(int width, int height, DdsFormat format) {
    var blockSize = GetBlockSize(format);
    if (blockSize > 0) {
      var blocksWide = Math.Max(1, (width + 3) / 4);
      var blocksHigh = Math.Max(1, (height + 3) / 4);
      return blocksWide * blocksHigh * blockSize;
    }

    var bitsPerPixel = format switch {
      DdsFormat.Rgba => 32,
      DdsFormat.Rgb => 24,
      _ => 32
    };

    return Math.Max(1, width) * Math.Max(1, height) * bitsPerPixel / 8;
  }
}
