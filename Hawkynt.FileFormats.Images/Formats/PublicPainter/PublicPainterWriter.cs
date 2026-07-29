using System;

namespace FileFormat.PublicPainter;

/// <summary>Assembles Public Painter (.cmp) file bytes from a PublicPainterFile.</summary>
public static class PublicPainterWriter {

  public static byte[] ToBytes(PublicPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = file.PixelData.AsSpan();
    var escape = PublicPainterCompressor.ChooseEscape(pixels);
    var stream = PublicPainterCompressor.Compress(pixels, escape);

    var result = new byte[PublicPainterFile.StreamOffset + stream.Length];
    result[PublicPainterFile.EscapeOffset] = escape;
    result[PublicPainterFile.HeightSelectorOffset] = PublicPainterFile.SingleHeightSelector;
    stream.CopyTo(result.AsSpan(PublicPainterFile.StreamOffset));

    return result;
  }
}
