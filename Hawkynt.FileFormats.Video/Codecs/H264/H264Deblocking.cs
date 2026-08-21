using System;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The deblocking filter — ITU-T H.264, clause 8.7.
/// </summary>
/// <remarks>
/// Not a post-process. The filtered picture is what later pictures are predicted from, so an error
/// here does not stay in the frame it happened in: it goes into the reference and comes back in every
/// frame predicted from it, growing. That is what makes this the part of an H.264 decoder most worth
/// checking hardest — it touches every reconstructed sample of every picture, and being nearly right
/// produces a picture that looks right and drifts.
/// <para/>
/// Three things decide what happens at an edge, and all three matter. <b>How strong</b>: a boundary
/// strength from 0 to 4, taken from what the two macroblocks are rather than from what their samples
/// look like — an edge between two intra macroblocks gets the strongest filter because the blocking
/// there is a coding artefact by construction, and an edge between two partitions with the same
/// motion and no residual gets none because there is nothing there to be an artefact.
/// <b>Whether at all</b>: two thresholds from the quantiser, so that a finely quantised picture is
/// left alone and a coarsely quantised one is not — a step across an edge larger than the quantiser
/// could have produced is a real edge in the picture and is not touched.
/// <b>How far</b>: at strength 4 up to three samples either side, elsewhere at most two, and only
/// where the samples just inside the block are flat enough that smoothing them destroys nothing.
/// <para/>
/// The whole picture is reconstructed before any of this runs, and then macroblocks are filtered in
/// raster order with each one reading what the ones before it left (clause 8.7, NOTE 1). So the
/// filter is sequential and in place, and the order of the edges within a macroblock — every vertical
/// one, left to right, then every horizontal one, top to bottom — is part of the specification rather
/// than an implementation choice.
/// </remarks>
internal static class H264Deblocking {

  /// <summary>Table 8-16: the threshold α for each indexA.</summary>
  private static readonly byte[] _ALPHA = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    4, 4, 5, 6, 7, 8, 9, 10, 12, 13, 15, 17, 20, 22, 25, 28,
    32, 36, 40, 45, 50, 56, 63, 71, 80, 90, 101, 113, 127, 144, 162, 182,
    203, 226, 255, 255,
  ];

  /// <summary>Table 8-16: the threshold β for each indexB.</summary>
  private static readonly byte[] _BETA = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 6, 6, 7, 7, 8, 8,
    9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16,
    17, 17, 18, 18,
  ];

  /// <summary>Table 8-17: t′C0 indexed by boundary strength minus one, then by indexA.</summary>
  private static readonly byte[,] _TC0 = {
    {
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 6, 6, 7, 8,
      9, 10, 11, 13,
    },
    {
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2,
      2, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 8, 10, 11,
      12, 13, 15, 17,
    },
    {
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3,
      3, 3, 4, 4, 4, 5, 6, 6, 7, 8, 9, 10, 11, 13, 14, 16,
      18, 20, 23, 25,
    },
  };

  /// <summary>Filters every edge of a reconstructed picture, macroblock by macroblock.</summary>
  internal static void Filter(H264FrameDecoder frame) {
    var mbWidth = frame.MacroblockWidth;
    var mbHeight = frame.MacroblockHeight;

    for (var mbY = 0; mbY < mbHeight; ++mbY)
      for (var mbX = 0; mbX < mbWidth; ++mbX) {
        var mbAddr = mbY * mbWidth + mbX;
        var idc = frame.DeblockingIdcOf(mbAddr);
        if (idc == 1)
          continue;

        // With idc 2 the edges between slices are left alone, which is what lets slices be decoded
        // independently of one another; with idc 0 they are filtered like any other (clause 8.7,
        // NOTE 1).
        var filterLeft = mbX > 0 && (idc != 2 || frame.SliceOf(mbAddr - 1) == frame.SliceOf(mbAddr));
        var filterTop = mbY > 0 && (idc != 2 || frame.SliceOf(mbAddr - mbWidth) == frame.SliceOf(mbAddr));

        _FilterLumaVertical(frame, mbAddr, mbX, mbY, filterLeft);
        _FilterLumaHorizontal(frame, mbAddr, mbX, mbY, filterTop);

        for (var component = 0; component < 2; ++component) {
          _FilterChromaVertical(frame, mbAddr, mbX, mbY, component, filterLeft);
          _FilterChromaHorizontal(frame, mbAddr, mbX, mbY, component, filterTop);
        }
      }
  }

  private static void _FilterLumaVertical(H264FrameDecoder frame, int mbAddr, int mbX, int mbY, bool filterLeft) {
    var picture = frame.Picture;

    for (var edge = 0; edge < 4; ++edge) {
      if (edge == 0 && !filterLeft)
        continue;

      var x = mbX * 16 + edge * 4;
      for (var group = 0; group < 4; ++group) {
        var y = mbY * 16 + group * 4;
        var strength = _BoundaryStrength(frame, x - 1, y, x, y, edge == 0);
        if (strength == 0)
          continue;

        var (alpha, beta, indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, x - 1, y), chroma: false, 0);
        for (var line = 0; line < 4; ++line)
          _FilterLine(picture.Luma, (y + line) * picture.LumaWidth + x, 1, strength, alpha, beta, indexA, chromaStyle: false);
      }
    }
  }

  private static void _FilterLumaHorizontal(H264FrameDecoder frame, int mbAddr, int mbX, int mbY, bool filterTop) {
    var picture = frame.Picture;

    for (var edge = 0; edge < 4; ++edge) {
      if (edge == 0 && !filterTop)
        continue;

      var y = mbY * 16 + edge * 4;
      for (var group = 0; group < 4; ++group) {
        var x = mbX * 16 + group * 4;
        var strength = _BoundaryStrength(frame, x, y - 1, x, y, edge == 0);
        if (strength == 0)
          continue;

        var (alpha, beta, indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, x, y - 1), chroma: false, 0);
        for (var line = 0; line < 4; ++line)
          _FilterLine(
            picture.Luma, y * picture.LumaWidth + x + line, picture.LumaWidth, strength, alpha, beta, indexA,
            chromaStyle: false);
      }
    }
  }

  private static void _FilterChromaVertical(
    H264FrameDecoder frame, int mbAddr, int mbX, int mbY, int component, bool filterLeft) {
    var picture = frame.Picture;
    var plane = picture.Chroma(component);

    // 4:2:0 filters two vertical chroma edges to the luma's four, at the macroblock edge and its
    // middle, because a chroma plane half the width has half as many block boundaries in it.
    for (var edge = 0; edge < 2; ++edge) {
      if (edge == 0 && !filterLeft)
        continue;

      var chromaX = mbX * 8 + edge * 4;
      var lumaX = chromaX * 2;

      // Two chroma rows at a time, not four. The strength is the luma edge's, taken at the luma
      // sample the chroma sample q0 sits on (clause 8.7.2), and a luma edge's strength changes every
      // four luma rows — which is every two chroma rows. Filtering four at a time would carry the
      // strength of the first luma block across the second, and the second is where a residual or a
      // change of motion most often appears.
      for (var group = 0; group < 4; ++group) {
        var chromaY = mbY * 8 + group * 2;
        var lumaY = chromaY * 2;

        var strength = _BoundaryStrength(frame, lumaX - 1, lumaY, lumaX, lumaY, edge == 0);
        if (strength == 0)
          continue;

        var (alpha, beta, indexA) = _Thresholds(
          frame, mbAddr, _MacroblockAt(frame, lumaX - 1, lumaY), chroma: true, component);

        for (var line = 0; line < 2; ++line)
          _FilterLine(
            plane, (chromaY + line) * picture.ChromaWidth + chromaX, 1, strength, alpha, beta, indexA,
            chromaStyle: true);
      }
    }
  }

  private static void _FilterChromaHorizontal(
    H264FrameDecoder frame, int mbAddr, int mbX, int mbY, int component, bool filterTop) {
    var picture = frame.Picture;
    var plane = picture.Chroma(component);

    for (var edge = 0; edge < 2; ++edge) {
      if (edge == 0 && !filterTop)
        continue;

      var chromaY = mbY * 8 + edge * 4;
      var lumaY = chromaY * 2;

      for (var group = 0; group < 4; ++group) {
        var chromaX = mbX * 8 + group * 2;
        var lumaX = chromaX * 2;

        var strength = _BoundaryStrength(frame, lumaX, lumaY - 1, lumaX, lumaY, edge == 0);
        if (strength == 0)
          continue;

        var (alpha, beta, indexA) = _Thresholds(
          frame, mbAddr, _MacroblockAt(frame, lumaX, lumaY - 1), chroma: true, component);

        for (var line = 0; line < 2; ++line)
          _FilterLine(
            plane, chromaY * picture.ChromaWidth + chromaX + line, picture.ChromaWidth, strength, alpha, beta, indexA,
            chromaStyle: true);
      }
    }
  }

  private static int _MacroblockAt(H264FrameDecoder frame, int lumaX, int lumaY)
    => lumaY / 16 * frame.MacroblockWidth + lumaX / 16;

  /// <summary>The boundary strength for one 4x4 block edge — clause 8.7.2.1.</summary>
  /// <remarks>
  /// The two-motion-vector cases of the clause cannot arise here: they describe a partition predicted
  /// from both reference lists, which only a B slice has, and B slices are refused. So the last test
  /// reduces to one reference picture and one vector on each side.
  /// </remarks>
  private static int _BoundaryStrength(
    H264FrameDecoder frame, int pX, int pY, int qX, int qY, bool macroblockEdge) {
    var pMb = _MacroblockAt(frame, pX, pY);
    var qMb = _MacroblockAt(frame, qX, qY);

    var pIntra = frame.KindOf(pMb) != H264MacroblockKind.Inter;
    var qIntra = frame.KindOf(qMb) != H264MacroblockKind.Inter;

    if (pIntra || qIntra)
      return macroblockEdge ? 4 : 3;

    if (frame.BlockHasCoefficients(pX >> 2, pY >> 2) || frame.BlockHasCoefficients(qX >> 2, qY >> 2))
      return 2;

    var p = frame.BlockMotion(pX >> 2, pY >> 2);
    var q = frame.BlockMotion(qX >> 2, qY >> 2);

    if (p.Predicted != q.Predicted || p.Reference != q.Reference)
      return 1;

    // Four quarter samples is one whole sample: a disagreement smaller than that cannot have put a
    // step across the edge worth filtering.
    return Math.Abs(p.X - q.X) >= 4 || Math.Abs(p.Y - q.Y) >= 4 ? 1 : 0;
  }

  /// <summary>The thresholds for one edge — clause 8.7.2.2.</summary>
  private static (int Alpha, int Beta, int IndexA) _Thresholds(
    H264FrameDecoder frame, int qMb, int pMb, bool chroma, int component) {
    var qpP = frame.QpOf(pMb);
    var qpQ = frame.QpOf(qMb);

    if (chroma) {
      qpP = H264Transform.ChromaQp(Math.Clamp(qpP + frame.ChromaQpOffsetOf(pMb, component), 0, 51));
      qpQ = H264Transform.ChromaQp(Math.Clamp(qpQ + frame.ChromaQpOffsetOf(qMb, component), 0, 51));
    }

    var average = (qpP + qpQ + 1) >> 1;

    // The offsets are the current slice's — the slice holding q0 — not the neighbour's, so two slices
    // may filter the edge between them by different amounts depending on which side is being decoded
    // (clause 8.7.2).
    var indexA = Math.Clamp(average + frame.FilterOffsetAOf(qMb), 0, 51);
    var indexB = Math.Clamp(average + frame.FilterOffsetBOf(qMb), 0, 51);

    return (_ALPHA[indexA], _BETA[indexB], indexA);
  }

  /// <summary>
  /// Filters one line of eight samples across an edge — clauses 8.7.2.3 and 8.7.2.4.
  /// </summary>
  /// <param name="plane">The sample plane.</param>
  /// <param name="q0At">The index of q0, the first sample on the far side of the edge.</param>
  /// <param name="step">The distance between consecutive samples across the edge: one for a vertical edge, a row for a horizontal one.</param>
  private static void _FilterLine(
    byte[] plane, int q0At, int step, int strength, int alpha, int beta, int indexA, bool chromaStyle) {
    var p0 = plane[q0At - step];
    var p1 = plane[q0At - 2 * step];
    var p2 = plane[q0At - 3 * step];
    var p3 = plane[q0At - 4 * step];
    var q0 = plane[q0At];
    var q1 = plane[q0At + step];
    var q2 = plane[q0At + 2 * step];
    var q3 = plane[q0At + 3 * step];

    // Equation 8-460: a step larger than the quantiser could have made is a real edge in the picture,
    // and none of the rest runs.
    if (Math.Abs(p0 - q0) >= alpha || Math.Abs(p1 - p0) >= beta || Math.Abs(q1 - q0) >= beta)
      return;

    var ap = Math.Abs(p2 - p0);
    var aq = Math.Abs(q2 - q0);

    if (strength < 4) {
      var tc0 = _TC0[strength - 1, indexA];
      var tc = chromaStyle ? tc0 + 1 : tc0 + (ap < beta ? 1 : 0) + (aq < beta ? 1 : 0);

      var delta = Math.Clamp((((q0 - p0) << 2) + (p1 - q1) + 4) >> 3, -tc, tc);
      plane[q0At - step] = _Clip(p0 + delta);
      plane[q0At] = _Clip(q0 - delta);

      if (!chromaStyle && ap < beta)
        plane[q0At - 2 * step] = (byte)(p1 + Math.Clamp((p2 + ((p0 + q0 + 1) >> 1) - (p1 << 1)) >> 1, -tc0, tc0));

      if (!chromaStyle && aq < beta)
        plane[q0At + step] = (byte)(q1 + Math.Clamp((q2 + ((p0 + q0 + 1) >> 1) - (q1 << 1)) >> 1, -tc0, tc0));

      return;
    }

    // Strength 4 is the macroblock edge of an intra macroblock, where the blocking is a coding
    // artefact by construction and the filter is allowed to reach three samples in — but only when
    // those samples are flat enough and the step is small enough that there is nothing there to lose
    // (equations 8-476 and 8-483).
    var wide = Math.Abs(p0 - q0) < (alpha >> 2) + 2;

    if (!chromaStyle && ap < beta && wide) {
      plane[q0At - step] = (byte)((p2 + 2 * p1 + 2 * p0 + 2 * q0 + q1 + 4) >> 3);
      plane[q0At - 2 * step] = (byte)((p2 + p1 + p0 + q0 + 2) >> 2);
      plane[q0At - 3 * step] = (byte)((2 * p3 + 3 * p2 + p1 + p0 + q0 + 4) >> 3);
    } else {
      plane[q0At - step] = (byte)((2 * p1 + p0 + q1 + 2) >> 2);
    }

    if (!chromaStyle && aq < beta && wide) {
      plane[q0At] = (byte)((p1 + 2 * p0 + 2 * q0 + 2 * q1 + q2 + 4) >> 3);
      plane[q0At + step] = (byte)((p0 + q0 + q1 + q2 + 2) >> 2);
      plane[q0At + 2 * step] = (byte)((2 * q3 + 3 * q2 + q1 + q0 + p0 + 4) >> 3);
    } else {
      plane[q0At] = (byte)((2 * q1 + q0 + p1 + 2) >> 2);
    }
  }

  private static byte _Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
