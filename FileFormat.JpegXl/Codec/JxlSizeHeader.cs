namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Bit-reader-based SizeHeader decoder (ISO/IEC 18181-1 §3.6.2). Mirrors
/// <see cref="FileFormat.JpegXl.JpegXlSizeHeader"/> but reads from a continuing
/// <see cref="JxlBitReader"/> instead of a byte span — required because real JXL
/// codestreams pack ImageMetadata immediately after SizeHeader at the bit level
/// (NOT byte-aligned), so the byte-based implementation can't be chained.
/// The byte-based version is kept for round-trip compatibility with the older
/// synthetic format.
/// </summary>
internal static class JxlSizeHeader {

  /// <summary>Decode width and height. Position advances past the SizeHeader bits.</summary>
  public static (int Width, int Height) Decode(JxlBitReader r) {
    // Per libjxl `headers.cc::SizeHeader::VisitFields`: Bool(false, &small_).
    // Bit value 1 = small mode (height divides by 8); bit value 0 = large.
    var small = r.ReadBool();
    if (small) {
      var heightDiv8 = (int)r.ReadBits(5);
      var height = (heightDiv8 + 1) * 8;
      var ratio = (int)r.ReadBits(3);
      var width = ratio == 0
        ? _ReadU32Dim(r)
        : _ApplyRatio(ratio, height);
      return (width, height);
    }

    var h = _ReadU32Dim(r);
    var ratioL = (int)r.ReadBits(3);
    var w = ratioL == 0 ? _ReadU32Dim(r) : _ApplyRatio(ratioL, h);
    return (w, h);
  }

  /// <summary>SizeHeader uses u32(BitsOffset(9,1), BitsOffset(13,1), BitsOffset(18,1), BitsOffset(30,1)).
  /// Each selector reads N bits then adds 1. Per libjxl `headers.cc::SizeHeader::VisitFields`.
  /// Selector 0 = 1 + u(9), selector 1 = 1 + u(13), selector 2 = 1 + u(18), selector 3 = 1 + u(30).
  /// JxlBitReader.ReadU32 signature is (c0, u0, c1, u1, c2, u2, c3, u3) so we pass the
  /// constants and bit-widths as (1, 9, 1, 13, 1, 18, 1, 30).</summary>
  private static int _ReadU32Dim(JxlBitReader r)
    => (int)(r.ReadU32(1, 9, 1, 13, 1, 18, 1, 30));

  private static int _ApplyRatio(int ratio, int height) => ratio switch {
    1 => height,
    2 => (int)((long)height * 12 / 10),
    3 => (int)((long)height * 4 / 3),
    4 => (int)((long)height * 3 / 2),
    5 => (int)((long)height * 16 / 9),
    6 => (int)((long)height * 5 / 4),
    7 => height * 2,
    _ => height,
  };
}
