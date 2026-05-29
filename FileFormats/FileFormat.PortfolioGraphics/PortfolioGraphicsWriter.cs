using System;

namespace FileFormat.PortfolioGraphics;

/// <summary>Assembles Atari Portfolio PGF format bytes from a <see cref="PortfolioGraphicsFile"/>.</summary>
public static class PortfolioGraphicsWriter {

  public static byte[] ToBytes(PortfolioGraphicsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PortfolioGraphicsFile.PgfFileSize];

    // 8-byte header (zeros)
    var copyLen = Math.Min(file.PixelData.Length, PortfolioGraphicsFile.PixelDataSize);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(PortfolioGraphicsFile.PgfHeaderSize));
    return result;
  }
}
