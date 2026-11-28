using System;
using FileFormat.LuraWave;

namespace FileFormat.LuraDocument;

/// <summary>Writes LuraDocument files, one or more pages.</summary>
/// <remarks>
/// <para>A black-and-white page becomes a mask alone, which is lossless.</para>
/// <para>
/// A grey or colour page becomes one full-size background layer with no mask, the layout LuraTech's encoder uses for its
/// "photo" mode; grey is lossless. Colour passes through the format's luma/colour-difference layer, which not every RGB
/// value survives: each pixel is stored as whichever layer value LuraTech's decoder turns into the nearest colour.
/// </para>
/// <para>The writer does not segment a page into foreground and background; it never writes thumbnails.</para>
/// </remarks>
public static class LuraDocumentWriter {

  /// <exception cref="ArgumentException">No pages, or a page whose pixels do not match its size.</exception>
  public static byte[] ToBytes(LuraDocumentFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pages.Count == 0)
      throw new ArgumentException("A LuraDocument file needs at least one page.", nameof(file));
    if (file.Pages.Count > ushort.MaxValue)
      throw new ArgumentException($"A LuraDocument file numbers its pages in 16 bits, so {file.Pages.Count} pages do not fit.", nameof(file));

    var writer = new LuraDocumentContainer.Writer();
    for (var index = 0; index < file.Pages.Count; ++index)
      _WritePage(writer, file.Pages[index], index, file.Pages.Count);
    return writer.ToBytes();
  }

  private static void _WritePage(LuraDocumentContainer.Writer writer, LuraDocumentPage page, int index, int count) {
    if (page.Width <= 0 || page.Height <= 0)
      throw new ArgumentException($"A LuraDocument page needs a non-empty size, not {page.Width}x{page.Height}.", nameof(page));
    var expected = (long)page.Width * page.Height * page.BytesPerPixel;
    if (page.PixelData is null || page.PixelData.Length != expected)
      throw new ArgumentException($"Page {index} needs {expected} bytes of {page.Kind} pixels for {page.Width}x{page.Height}.", nameof(page));

    var horizontalDpi = page.HorizontalDpi > 0 ? page.HorizontalDpi : LuraDocumentFile.DefaultDpi;
    var verticalDpi = page.VerticalDpi > 0 ? page.VerticalDpi : LuraDocumentFile.DefaultDpi;

    writer.Begin(opensPage: true);
    writer.Long(LuraDocumentContainer.TagWidth, page.Width);
    writer.Long(LuraDocumentContainer.TagHeight, page.Height);
    writer.Ascii(LuraDocumentContainer.TagDocumentName, "");
    writer.Ascii(LuraDocumentContainer.TagImageDescription, "");
    writer.Long(LuraDocumentContainer.TagStripOffsets, 0);
    writer.Short(LuraDocumentContainer.TagOrientation, 1);
    writer.Long(LuraDocumentContainer.TagStripByteCounts, 0);
    writer.Rational(LuraDocumentContainer.TagXResolution, (uint)horizontalDpi, 1);
    writer.Rational(LuraDocumentContainer.TagYResolution, (uint)verticalDpi, 1);
    writer.Add(LuraDocumentContainer.TagPageNumber, 3, 2, [(byte)index, (byte)(index >> 8), (byte)count, (byte)(count >> 8)]);
    // LuraTech's writer puts four bytes 01 01 00 00 under tag 305; kept for its decoder's sake.
    writer.Add(LuraDocumentContainer.TagSoftware, 1, 4, [1, 1, 0, 0]);
    writer.Ascii(LuraDocumentContainer.TagIptc, "");
    writer.Ascii(LuraDocumentContainer.TagLayerName, "bit");
    writer.Ascii(LuraDocumentContainer.TagAnnotations, "");

    if (page.Kind == LuraDocumentPageKind.Bilevel) {
      writer.Short(LuraDocumentContainer.TagBitsPerSample, 1);
      writer.Short(LuraDocumentContainer.TagCompression, LuraDocumentContainer.CompressionMask);
      writer.Short(LuraDocumentContainer.TagPhotometric, 1);
      writer.Short(LuraDocumentContainer.TagSamplesPerPixel, 1);
      var stride = (page.Width + 7) >> 3;
      var rows = new byte[stride * page.Height];
      for (var y = 0; y < page.Height; ++y)
      for (var x = 0; x < page.Width; ++x)
        if (page.PixelData[y * page.Width + x] != 0)
          rows[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));

      var bie = LuraDocumentJbig.Encode(rows, page.Width, page.Height);
      LuraDocumentMask.Apply(bie, LuraDocumentMask.Seed(Math.Max(horizontalDpi, verticalDpi), page.Width, page.Height));
      writer.Strip(bie);
      return;
    }

    var colour = page.Kind == LuraDocumentPageKind.Colour;
    var samples = new byte[page.PixelData.Length];
    if (colour)
      for (var i = 0; i < samples.Length; i += 3) {
        var (y, u, v) = LuraDocumentColour.FromRgb(page.PixelData[i], page.PixelData[i + 1], page.PixelData[i + 2]);
        samples[i] = (byte)(255 - y);
        samples[i + 1] = u;
        samples[i + 2] = v;
      }
    else
      for (var i = 0; i < samples.Length; ++i)
        samples[i] = (byte)(255 - page.PixelData[i]);

    writer.Begin(opensPage: false);
    writer.Long(LuraDocumentContainer.TagWidth, page.Width);
    writer.Long(LuraDocumentContainer.TagHeight, page.Height);
    writer.Short(LuraDocumentContainer.TagBitsPerSample, 8);
    writer.Short(LuraDocumentContainer.TagCompression, LuraDocumentContainer.CompressionLuraWave);
    writer.Short(LuraDocumentContainer.TagPhotometric, colour ? 2 : 1);
    writer.Long(LuraDocumentContainer.TagStripOffsets, 0);
    writer.Short(LuraDocumentContainer.TagSamplesPerPixel, colour ? 3 : 1);
    writer.Long(LuraDocumentContainer.TagStripByteCounts, 0);
    writer.Ascii(LuraDocumentContainer.TagLayerName, "bac");
    writer.Strip(LuraWaveCodec.Encode(samples, page.Width, page.Height, colour ? LuraWaveColourMode.Rgb : LuraWaveColourMode.Grey));
  }
}
