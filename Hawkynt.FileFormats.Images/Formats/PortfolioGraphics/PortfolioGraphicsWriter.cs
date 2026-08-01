using System;

namespace FileFormat.PortfolioGraphics;

/// <summary>Assembles Atari Portfolio PGF format bytes from a <see cref="PortfolioGraphicsFile"/>.</summary>
public static class PortfolioGraphicsWriter {

  public static byte[] ToBytes(PortfolioGraphicsFile file) {
    var result = new byte[PortfolioGraphicsFile.PgfFileSize];
    var bitmap = file.PixelData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, result.Length)).CopyTo(result);
    return result;
  }
}
