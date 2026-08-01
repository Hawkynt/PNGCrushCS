using System;

namespace FileFormat.Core;

/// <summary>One bit a pixel, with each row starting on a fresh byte.</summary>
/// <remarks>
/// Nearly every bilevel format pads its rows to a whole number of bytes, because that is what makes
/// a row addressable. <see cref="PixelFormat.Indexed1"/> does not: it is one continuous stream of
/// bits with nothing between the rows. Handing a padded picture over as if it were continuous costs
/// nothing on the first row and slides every row after it by the width of the padding — a picture
/// that leans, rather than one that is obviously broken, which is why it kept getting through.
/// <para/>
/// So padded rows are spread to a byte a pixel on the way in and packed again on the way out. The
/// cost is one byte per pixel in memory; the gain is that the row stride stops being implicit.
/// </remarks>
public static class BilevelRows {

  /// <summary>How many bytes one row of a given width takes.</summary>
  public static int Stride(int width) => PackedRows.Stride(width, 1);

  /// <summary>Spreads byte-padded rows of bits to one index a pixel.</summary>
  /// <param name="packed">The rows, each starting on a byte boundary.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="mostSignificantFirst">
  /// Whether the leftmost pixel of a byte is its top bit. True for most formats; X bitmaps and the
  /// things that copied them fill from the bottom bit instead.
  /// </param>
  public static byte[] Unpack(ReadOnlySpan<byte> packed, int width, int height, bool mostSignificantFirst = true)
    => PackedRows.Unpack(packed, width, height, 1, mostSignificantFirst: mostSignificantFirst);

  /// <summary>Reduces a picture to one bit a pixel by brightness.</summary>
  /// <param name="image">The picture to reduce.</param>
  /// <param name="setWhenDark">
  /// Whether a dark pixel is the one that gets the bit. Formats disagree on this: a set bit means
  /// ink in an X bitmap and light in a WAP one, and the two look identical until compared.
  /// </param>
  public static byte[] Threshold(RawImage image, bool setWhenDark) {
    ArgumentNullException.ThrowIfNull(image);

    var gray = PixelConverter.Convert(image, PixelFormat.Gray8);
    var pixels = new byte[gray.Width * gray.Height];
    for (var i = 0; i < pixels.Length && i < gray.PixelData.Length; ++i)
      pixels[i] = (byte)(gray.PixelData[i] < 128 == setWhenDark ? 1 : 0);

    return pixels;
  }

  /// <summary>Packs one index a pixel back into byte-padded rows.</summary>
  /// <remarks>Any index other than zero counts as set, so an eight-bit mask packs as well as a bit.</remarks>
  public static byte[] Pack(ReadOnlySpan<byte> pixels, int width, int height, bool mostSignificantFirst = true)
    => PackedRows.Pack(pixels, width, height, 1, mostSignificantFirst: mostSignificantFirst);
}
