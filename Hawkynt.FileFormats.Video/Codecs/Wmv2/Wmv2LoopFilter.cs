using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.Wmv2;

/// <summary>
/// The deblocking filter a WMV2 stream whose extension header asks for it runs over its intra and
/// predicted pictures: H.263's Annex J filter, in the order FFmpeg applies it.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>ff_h263_loop_filter</c> (<c>libavcodec/h263.c</c>) and
/// <c>h263_h_loop_filter_c</c> / <c>h263_v_loop_filter_c</c> (<c>h263dsp.c</c>), LGPL-2.1-or-later.
/// FFmpeg filters each macroblock as soon as it is reconstructed, touching only macroblocks above and
/// to the left of it; nothing a later macroblock reads comes out of the picture being built, so running
/// the same calls in the same order once the picture is whole gives the same samples. A skipped
/// macroblock is filtered with a quantiser of nought, which turns the edges between two skipped ones off.
/// </remarks>
internal static class Wmv2LoopFilter {

  /// <summary><c>ff_h263_loop_filter_strength</c>.</summary>
  private static readonly byte[] _Strength = [
    0, 1, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 7,
    7, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 11, 12, 12, 12,
  ];

  /// <summary>Filters a whole picture, macroblock by macroblock in raster order.</summary>
  /// <param name="frame">The picture, in place.</param>
  /// <param name="macroblockWidth">Its width in macroblocks.</param>
  /// <param name="macroblockHeight">Its height in macroblocks.</param>
  /// <param name="quantiser">The picture's quantiser, which is every macroblock's.</param>
  /// <param name="skipped">Which macroblocks were skipped, or <c>null</c> where none were.</param>
  internal static void Apply(Mpeg4Frame frame, int macroblockWidth, int macroblockHeight, int quantiser, bool[]? skipped) {
    var luma = frame.Luma;
    var ls = frame.LumaStride;
    var cs = frame.ChromaStride;

    for (var row = 0; row < macroblockHeight; ++row)
      for (var column = 0; column < macroblockWidth; ++column) {
        var address = row * macroblockWidth + column;
        bool IsSkipped(int a) => skipped != null && skipped[a];

        var y = frame.LumaOrigin + row * 16 * ls + column * 16;
        var cb = frame.ChromaOrigin + row * 8 * cs + column * 8;

        int qpC;
        if (!IsSkipped(address)) {
          qpC = quantiser;
          _Vertical(luma, y + 8 * ls, ls, qpC);
          _Vertical(luma, y + 8 * ls + 8, ls, qpC);
        } else {
          qpC = 0;
        }

        if (row > 0) {
          var qpTt = IsSkipped(address - macroblockWidth) ? 0 : quantiser;
          var qpTc = qpC != 0 ? qpC : qpTt;

          if (qpTc != 0) {
            _Vertical(luma, y, ls, qpTc);
            _Vertical(luma, y + 8, ls, qpTc);
            _Vertical(frame.Cb, cb, cs, qpTc);
            _Vertical(frame.Cr, cb, cs, qpTc);
          }

          if (qpTt != 0)
            _Horizontal(luma, y - 8 * ls + 8, ls, qpTt);

          if (column > 0) {
            var qpDt = qpTt != 0 || IsSkipped(address - 1 - macroblockWidth) ? qpTt : quantiser;
            if (qpDt != 0) {
              _Horizontal(luma, y - 8 * ls, ls, qpDt);
              _Horizontal(frame.Cb, cb - 8 * cs, cs, qpDt);
              _Horizontal(frame.Cr, cb - 8 * cs, cs, qpDt);
            }
          }
        }

        if (qpC != 0) {
          _Horizontal(luma, y + 8, ls, qpC);
          if (row + 1 == macroblockHeight)
            _Horizontal(luma, y + 8 * ls + 8, ls, qpC);
        }

        if (column > 0) {
          var qpLc = qpC != 0 || IsSkipped(address - 1) ? qpC : quantiser;
          if (qpLc != 0) {
            _Horizontal(luma, y, ls, qpLc);
            if (row + 1 == macroblockHeight) {
              _Horizontal(luma, y + 8 * ls, ls, qpLc);
              _Horizontal(frame.Cb, cb, cs, qpLc);
              _Horizontal(frame.Cr, cb, cs, qpLc);
            }
          }
        }
      }
  }

  /// <summary><c>h263_h_loop_filter_c</c>: eight rows across the vertical edge just left of <paramref name="origin"/>.</summary>
  private static void _Horizontal(byte[] plane, int origin, int stride, int quantiser) {
    for (var y = 0; y < 8; ++y)
      _Filter(plane, origin + y * stride, 1, quantiser);
  }

  /// <summary><c>h263_v_loop_filter_c</c>: eight columns across the horizontal edge just above <paramref name="origin"/>.</summary>
  private static void _Vertical(byte[] plane, int origin, int stride, int quantiser) {
    for (var x = 0; x < 8; ++x)
      _Filter(plane, origin + x, stride, quantiser);
  }

  private static void _Filter(byte[] plane, int at, int step, int quantiser) {
    var strength = _Strength[quantiser];
    int p0 = plane[at - 2 * step];
    int p1 = plane[at - step];
    int p2 = plane[at];
    int p3 = plane[at + step];
    var d = (p0 - p3 + 4 * (p2 - p1)) / 8;

    int d1;
    if (d < -2 * strength)
      d1 = 0;
    else if (d < -strength)
      d1 = -2 * strength - d;
    else if (d < strength)
      d1 = d;
    else if (d < 2 * strength)
      d1 = 2 * strength - d;
    else
      d1 = 0;

    p1 += d1;
    p2 -= d1;
    if ((p1 & 256) != 0)
      p1 = ~(p1 >> 31);
    if ((p2 & 256) != 0)
      p2 = ~(p2 >> 31);

    plane[at - step] = unchecked((byte)p1);
    plane[at] = unchecked((byte)p2);

    var ad1 = System.Math.Abs(d1) >> 1;
    var d2 = System.Math.Clamp((p0 - p3) / 4, -ad1, ad1);

    plane[at - 2 * step] = unchecked((byte)(p0 - d2));
    plane[at + step] = unchecked((byte)(p3 + d2));
  }
}
