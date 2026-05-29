using System;

namespace FileFormat.PublicPainter;

/// <summary>Assembles Public Painter (.cmp) file bytes from a PublicPainterFile.</summary>
public static class PublicPainterWriter {

  public static byte[] ToBytes(PublicPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return PublicPainterCompressor.Compress(file.PixelData.AsSpan());
  }
}
