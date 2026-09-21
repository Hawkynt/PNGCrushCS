using System;

namespace FileFormat.Core;

/// <summary>Rows of pixels narrower than a byte, each row starting on a fresh byte.</summary>
/// <remarks>
/// A format that spends one, two or four bits a pixel almost always pads each row out to a whole
/// number of bytes, so that a row has an address. Several go further and store a row stride of their
/// own that is wider still. Neither is expressible in an <c>Indexed*</c> pixel format here, which is
/// a continuous stream with nothing between the rows.
/// <para/>
/// The difference shows only from the second row on, as a picture that leans by the width of the
/// padding — and it round-trips through the same format's own writer without complaint. So the
/// packing is undone on the way in and redone on the way out, and the stride is stated rather than
/// assumed.
/// </remarks>
public static class PackedRows {

  /// <summary>The tightest row stride for a width at a given depth.</summary>
  public static int Stride(int width, int bitsPerPixel) => (width * bitsPerPixel + 7) >> 3;

  /// <summary>Spreads packed rows to one index a pixel.</summary>
  /// <param name="packed">The rows.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="bitsPerPixel">One, two or four.</param>
  /// <param name="stride">
  /// Bytes from one row to the next. Zero means the tightest that fits, which is what a format that
  /// does not say otherwise uses.
  /// </param>
  /// <param name="mostSignificantFirst">Whether the leftmost pixel of a byte is its top bits.</param>
  public static byte[] Unpack(
    ReadOnlySpan<byte> packed, int width, int height, int bitsPerPixel, int stride = 0,
    bool mostSignificantFirst = true) {
    if (bitsPerPixel is not (1 or 2 or 4))
      throw new ArgumentOutOfRangeException(
        nameof(bitsPerPixel), bitsPerPixel, "Only depths below a byte are packed.");

    if (stride <= 0)
      stride = Stride(width, bitsPerPixel);

    var perByte = 8 / bitsPerPixel;
    var mask = (1 << bitsPerPixel) - 1;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = y * stride + x / perByte;
      if (at >= packed.Length)
        return pixels;

      var slot = x % perByte;
      var shift = (mostSignificantFirst ? perByte - 1 - slot : slot) * bitsPerPixel;
      pixels[y * width + x] = (byte)((packed[at] >> shift) & mask);
    }

    return pixels;
  }

  /// <summary>Packs one index a pixel back into rows, clamping each index to the depth.</summary>
  public static byte[] Pack(
    ReadOnlySpan<byte> pixels, int width, int height, int bitsPerPixel, int stride = 0,
    bool mostSignificantFirst = true) {
    if (bitsPerPixel is not (1 or 2 or 4))
      throw new ArgumentOutOfRangeException(
        nameof(bitsPerPixel), bitsPerPixel, "Only depths below a byte are packed.");

    if (stride <= 0)
      stride = Stride(width, bitsPerPixel);

    var perByte = 8 / bitsPerPixel;
    var mask = (1 << bitsPerPixel) - 1;
    var packed = new byte[stride * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var from = y * width + x;
      if (from >= pixels.Length)
        return packed;

      var slot = x % perByte;
      var shift = (mostSignificantFirst ? perByte - 1 - slot : slot) * bitsPerPixel;
      packed[y * stride + x / perByte] |= (byte)((pixels[from] & mask) << shift);
    }

    return packed;
  }

  /// <summary>Restacks rows that each start on a byte boundary into the continuous stream behind them.</summary>
  /// <param name="padded">The rows as the file stores them, each starting on a fresh byte.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="bitsPerPixel">One, two or four.</param>
  /// <param name="stride">
  /// Bytes from one row to the next. Zero means the tightest that fits, which is what a format that
  /// pads only to a whole byte uses; BMP and ICO pad to four bytes and Sun rasters to two, and those
  /// state their own.
  /// </param>
  /// <remarks>
  /// This is the boundary between the two layouts. On disk a row has an address; in an
  /// <c>Indexed1</c> or <c>Indexed4</c> <see cref="RawImage"/> it does not, because the indices run
  /// straight on across the picture. Handing the stored form over unchanged costs nothing on the
  /// first row and slides every row after it by the padding, which is a picture that leans rather
  /// than one that is obviously broken.
  /// </remarks>
  public static byte[] DropRowPadding(
    ReadOnlySpan<byte> padded, int width, int height, int bitsPerPixel, int stride = 0) {
    if (bitsPerPixel is not (1 or 2 or 4))
      throw new ArgumentOutOfRangeException(
        nameof(bitsPerPixel), bitsPerPixel, "Only depths below a byte are padded.");

    if (stride <= 0)
      stride = Stride(width, bitsPerPixel);

    // A row that ends exactly on a byte boundary, stored at that stride, already is the continuous
    // form. Nearly every picture is, which is why the difference took so long to show.
    if (stride * 8 == width * bitsPerPixel)
      return padded.ToArray();

    var result = new byte[((long)width * height * bitsPerPixel + 7) / 8];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = y * stride * 8 + x * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= padded.Length)
        return result;

      var value = (padded[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = (y * width + x) * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
  }

  /// <summary>Restacks a continuous stream into rows that each start on a byte boundary.</summary>
  /// <param name="pixels">The indices running straight on across the picture.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="bitsPerPixel">One, two or four.</param>
  /// <param name="stride">Bytes from one row to the next, or zero for the tightest that fits.</param>
  /// <remarks>The inverse of <see cref="DropRowPadding"/>; the padding bits are left clear.</remarks>
  public static byte[] AddRowPadding(
    ReadOnlySpan<byte> pixels, int width, int height, int bitsPerPixel, int stride = 0) {
    if (bitsPerPixel is not (1 or 2 or 4))
      throw new ArgumentOutOfRangeException(
        nameof(bitsPerPixel), bitsPerPixel, "Only depths below a byte are padded.");

    if (stride <= 0)
      stride = Stride(width, bitsPerPixel);

    if (stride * 8 == width * bitsPerPixel)
      return pixels.ToArray();

    var result = new byte[(long)stride * height];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = (y * width + x) * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= pixels.Length)
        return result;

      var value = (pixels[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = y * stride * 8 + x * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
  }

  /// <summary>Drops the padding between rows of whole bytes, leaving them back to back.</summary>
  public static byte[] Compact(ReadOnlySpan<byte> packed, int width, int height, int bytesPerPixel, int stride) {
    var tight = width * bytesPerPixel;
    if (stride <= 0 || stride == tight)
      return packed.ToArray();

    var pixels = new byte[tight * height];
    for (var y = 0; y < height; ++y) {
      var from = y * stride;
      if (from + tight > packed.Length)
        break;

      packed.Slice(from, tight).CopyTo(pixels.AsSpan(y * tight));
    }

    return pixels;
  }
}
