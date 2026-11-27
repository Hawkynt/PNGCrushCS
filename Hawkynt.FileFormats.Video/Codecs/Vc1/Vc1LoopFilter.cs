using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// The deblocking filter of a progressive I picture (SMPTE 421M 8.6): every horizontal 8x8 block edge
/// first, then every vertical one, inside the picture, with the picture quantiser as the threshold.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>ff_vc1_i_loop_filter</c> (<c>libavcodec/vc1_loopfilter.c</c>) and
/// <c>vc1_loop_filter</c> / <c>vc1_filter_line</c> (<c>vc1dsp.c</c>), LGPL-2.1-or-later. FFmpeg runs it a
/// row and a column behind the decode so that, within every macroblock, the vertical filtering happens
/// before the horizontal; the edges are eight samples apart and the filter reads four samples either
/// side and writes one, so filtering every horizontal edge of the picture and then every vertical one
/// touches each sample in the same order and gives the same picture.
/// </remarks>
internal static class Vc1LoopFilter {

  /// <summary>Filters the frame's three planes in place; the samples are already clamped.</summary>
  internal static void ApplyIntra(Vc1Frame frame, int quantiser) {
    _Plane(frame.Luma, frame.LumaWidth, frame.LumaHeight, quantiser);
    _Plane(frame.Cb, frame.ChromaWidth, frame.ChromaHeight, quantiser);
    _Plane(frame.Cr, frame.ChromaWidth, frame.ChromaHeight, quantiser);
  }

  private static void _Plane(int[] plane, int width, int height, int quantiser) {
    for (var y = 8; y < height; y += 8)
      for (var x = 0; x < width; x += 4)
        _Group(plane, y * width + x, 1, width, quantiser);

    for (var x = 8; x < width; x += 8)
      for (var y = 0; y < height; y += 4)
        _Group(plane, y * width + x, width, 1, quantiser);
  }

  /// <summary>Four lines across an edge: the third decides whether the other three are filtered.</summary>
  private static void _Group(int[] plane, int at, int step, int across, int quantiser) {
    if (_Line(plane, at + 2 * step, across, quantiser)) {
      _Line(plane, at, across, quantiser);
      _Line(plane, at + step, across, quantiser);
      _Line(plane, at + 3 * step, across, quantiser);
    }
  }

  /// <summary><c>vc1_filter_line</c>: whether the edge here was smooth enough to filter at all.</summary>
  private static bool _Line(int[] s, int at, int stride, int pq) {
    var a0 = (2 * (s[at - 2 * stride] - s[at + stride]) - 5 * (s[at - stride] - s[at]) + 4) >> 3;
    var a0Sign = a0 >> 31;
    a0 = (a0 ^ a0Sign) - a0Sign;
    if (a0 >= pq)
      return false;

    var a1 = Math.Abs((2 * (s[at - 4 * stride] - s[at - stride]) - 5 * (s[at - 3 * stride] - s[at - 2 * stride]) + 4) >> 3);
    var a2 = Math.Abs((2 * (s[at] - s[at + 3 * stride]) - 5 * (s[at + stride] - s[at + 2 * stride]) + 4) >> 3);
    if (a1 >= a0 && a2 >= a0)
      return false;

    var clip = s[at - stride] - s[at];
    var clipSign = clip >> 31;
    clip = ((clip ^ clipSign) - clipSign) >> 1;
    if (clip == 0)
      return false;

    var d = 5 * (Math.Min(a1, a2) - a0);
    var dSign = d >> 31;
    d = ((d ^ dSign) - dSign) >> 3;
    dSign ^= a0Sign;

    if ((dSign ^ clipSign) == 0) {
      d = Math.Min(d, clip);
      d = (d ^ dSign) - dSign;
      s[at - stride] = Math.Clamp(s[at - stride] - d, 0, 255);
      s[at] = Math.Clamp(s[at] + d, 0, 255);
    }

    return true;
  }
}
