using System;
using FileFormat.Codecs.Vp8;

namespace FileFormat.Codecs.Vp7;

/// <summary>
/// VP7's loop filter, run over the whole reconstructed frame in macroblock order.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>filter_mb</c>, <c>filter_mb_simple</c> and <c>filter_level_for_mb</c>
/// in <c>libavcodec/vp8.c</c> and the <c>vp7</c> loop filters of <c>vp8dsp.c</c>, LGPL-2.1-or-later.
/// Against VP8 it differs in four places: the edge limits are the filter level plus two at a
/// macroblock edge and the level itself inside one; the simple threshold compares only the two
/// samples either side of the edge; the common filter rounds its second tap down by one where the
/// filter value is four modulo eight; and the inner edges between columns are filtered after those
/// between rows rather than before. Every macroblock filters its inner edges, coded residue or not.
/// </remarks>
internal static class Vp7LoopFilter {

  private static ReadOnlySpan<byte> _HighEdgeVarianceInter => [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    3, 3, 3, 3,
  ];

  private static ReadOnlySpan<byte> _HighEdgeVarianceKey => [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2,
  ];

  internal static void Apply(Vp8Frame frame, int level, int sharpness, bool simple, bool isKeyFrame) {
    if (level == 0)
      return;

    var interior = level;
    if (sharpness != 0) {
      interior >>= (sharpness + 3) >> 2;
      interior = Math.Min(interior, 9 - sharpness);
    }
    interior = Math.Max(interior, 1);
    var hev = isKeyFrame ? _HighEdgeVarianceKey[level] : _HighEdgeVarianceInter[level];

    var columns = frame.LumaWidth / 16;
    var rows = frame.LumaHeight / 16;
    var luma = frame.Luma;
    var ls = frame.LumaWidth;
    var cs = frame.ChromaWidth;

    for (var row = 0; row < rows; ++row)
      for (var column = 0; column < columns; ++column) {
        var y = row * 16 * ls + column * 16;
        if (simple) {
          var inner = 2 * level + interior;
          var edge = inner + 4;
          if (column > 0)
            _Simple(luma, y, ls, 1, edge);
          for (var i = 4; i < 16; i += 4)
            _Simple(luma, y + i, ls, 1, inner);
          if (row > 0)
            _Simple(luma, y, 1, ls, edge);
          for (var i = 4; i < 16; i += 4)
            _Simple(luma, y + i * ls, 1, ls, inner);
          continue;
        }

        var c = row * 8 * cs + column * 8;
        var macroblockEdge = level + 2;
        var innerLuma = level;
        var innerChroma = level * 2;

        if (column > 0) {
          _Normal(luma, y, ls, 1, 16, macroblockEdge, interior, hev, inner: false);
          _Normal(frame.Cb, c, cs, 1, 8, macroblockEdge, interior, hev, inner: false);
          _Normal(frame.Cr, c, cs, 1, 8, macroblockEdge, interior, hev, inner: false);
        }

        if (row > 0) {
          _Normal(luma, y, 1, ls, 16, macroblockEdge, interior, hev, inner: false);
          _Normal(frame.Cb, c, 1, cs, 8, macroblockEdge, interior, hev, inner: false);
          _Normal(frame.Cr, c, 1, cs, 8, macroblockEdge, interior, hev, inner: false);
        }

        for (var i = 4; i < 16; i += 4)
          _Normal(luma, y + i * ls, 1, ls, 16, innerLuma, interior, hev, inner: true);
        _Normal(frame.Cb, c + 4 * cs, 1, cs, 8, innerChroma, interior, hev, inner: true);
        _Normal(frame.Cr, c + 4 * cs, 1, cs, 8, innerChroma, interior, hev, inner: true);

        for (var i = 4; i < 16; i += 4)
          _Normal(luma, y + i, ls, 1, 16, innerLuma, interior, hev, inner: true);
        _Normal(frame.Cb, c + 4, cs, 1, 8, innerChroma, interior, hev, inner: true);
        _Normal(frame.Cr, c + 4, cs, 1, 8, innerChroma, interior, hev, inner: true);
      }
  }

  /// <summary>
  /// Filters <paramref name="count"/> lines across one edge. <paramref name="along"/> steps from one
  /// line to the next and <paramref name="across"/> from one sample to the next across the edge,
  /// which sits between <c>at - across</c> and <c>at</c>.
  /// </summary>
  private static void _Normal(byte[] p, int at, int along, int across, int count, int edgeLimit, int interior, int hev, bool inner) {
    for (var i = 0; i < count; ++i, at += along) {
      int p3 = p[at - 4 * across], p2 = p[at - 3 * across], p1 = p[at - 2 * across], p0 = p[at - across];
      int q0 = p[at], q1 = p[at + across], q2 = p[at + 2 * across], q3 = p[at + 3 * across];
      if (Math.Abs(p0 - q0) > edgeLimit
          || Math.Abs(p3 - p2) > interior || Math.Abs(p2 - p1) > interior || Math.Abs(p1 - p0) > interior
          || Math.Abs(q3 - q2) > interior || Math.Abs(q2 - q1) > interior || Math.Abs(q1 - q0) > interior)
        continue;

      var highVariance = Math.Abs(p1 - p0) > hev || Math.Abs(q1 - q0) > hev;
      if (highVariance)
        _Common(p, at, across, fourTap: true);
      else if (inner)
        _Common(p, at, across, fourTap: false);
      else {
        var w = _Clip8(_Clip8(p1 - q1) + 3 * (q0 - p0));
        var a0 = (27 * w + 63) >> 7;
        var a1 = (18 * w + 63) >> 7;
        var a2 = (9 * w + 63) >> 7;
        p[at - 3 * across] = _Clamp(p2 + a2);
        p[at - 2 * across] = _Clamp(p1 + a1);
        p[at - across] = _Clamp(p0 + a0);
        p[at] = _Clamp(q0 - a0);
        p[at + across] = _Clamp(q1 - a1);
        p[at + 2 * across] = _Clamp(q2 - a2);
      }
    }
  }

  private static void _Simple(byte[] p, int at, int along, int across, int limit) {
    for (var i = 0; i < 16; ++i, at += along)
      if (Math.Abs(p[at - across] - p[at]) <= limit)
        _Common(p, at, across, fourTap: true);
  }

  private static void _Common(byte[] p, int at, int across, bool fourTap) {
    int p1 = p[at - 2 * across], p0 = p[at - across], q0 = p[at], q1 = p[at + across];
    var a = 3 * (q0 - p0);
    if (fourTap)
      a += _Clip8(p1 - q1);
    a = _Clip8(a);

    var f1 = Math.Min(a + 4, 127) >> 3;
    var f2 = f1 - ((a & 7) == 4 ? 1 : 0);
    p[at - across] = _Clamp(p0 + f2);
    p[at] = _Clamp(q0 - f1);

    if (!fourTap) {
      a = (f1 + 1) >> 1;
      p[at - 2 * across] = _Clamp(p1 + a);
      p[at + across] = _Clamp(q1 - a);
    }
  }

  private static int _Clip8(int value) => value < -128 ? -128 : value > 127 ? 127 : value;

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
