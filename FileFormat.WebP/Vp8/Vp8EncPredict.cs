using System;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// Stateless intra prediction primitives for the VP8 encoder.
/// Input: top row (may be null for mby=0), left column (may be null for mbx=0), top-left corner.
/// Output: predicted block written linearly to <c>dst[size*size]</c>.
/// Convention matches RFC 6386 §12 and the decoder's predFunc16/8 (see Vp8Decoder.Pred.cs):
/// when top row is absent samples default to 127; when left column is absent samples default to 129.
/// </summary>
internal static class Vp8EncPredict {

  // Mode IDs (must match Vp8Decoder.Pred.cs constants).
  public const byte DC_PRED = 0;
  public const byte TM_PRED = 1;
  public const byte V_PRED = 2;
  public const byte H_PRED = 3;

  // --- 16x16 luma predictors ---

  public static void Predict16(byte mode, byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    switch (mode) {
      case DC_PRED: _DcPredict(dst, 16, top, left); break;
      case TM_PRED: _TmPredict(dst, 16, top, left, topLeft); break;
      case V_PRED: _VerticalPredict(dst, 16, top); break;
      case H_PRED: _HorizontalPredict(dst, 16, left); break;
      default: throw new ArgumentOutOfRangeException(nameof(mode));
    }
  }

  // --- 8x8 chroma predictors ---

  public static void Predict8(byte mode, byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    switch (mode) {
      case DC_PRED: _DcPredict(dst, 8, top, left); break;
      case TM_PRED: _TmPredict(dst, 8, top, left, topLeft); break;
      case V_PRED: _VerticalPredict(dst, 8, top); break;
      case H_PRED: _HorizontalPredict(dst, 8, left); break;
      default: throw new ArgumentOutOfRangeException(nameof(mode));
    }
  }

  // --- 4x4 luma predictors (all 10 modes) ---

  public const byte B_DC_PRED = 0;
  public const byte B_TM_PRED = 1;
  public const byte B_VE_PRED = 2;
  public const byte B_HE_PRED = 3;
  public const byte B_RD_PRED = 4;
  public const byte B_VR_PRED = 5;
  public const byte B_LD_PRED = 6;
  public const byte B_VL_PRED = 7;
  public const byte B_HD_PRED = 8;
  public const byte B_HU_PRED = 9;

  /// <summary>Predict a 4x4 block. <paramref name="top"/> should provide 8 samples
  /// (top[0..3] above, top[4..7] top-right extension used by LD/VL); <paramref name="left"/> provides
  /// 4 samples (left column); <paramref name="topLeft"/> is the single corner sample.
  /// Any null pointer indicates "no samples available" — spec defaults apply (127 for top, 129 for left).</summary>
  public static void Predict4(byte mode, byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    switch (mode) {
      case B_DC_PRED: _Predict4Dc(dst, top, left); break;
      case B_TM_PRED: _Predict4Tm(dst, top, left, topLeft); break;
      case B_VE_PRED: _Predict4Ve(dst, top, topLeft); break;
      case B_HE_PRED: _Predict4He(dst, left, topLeft); break;
      case B_RD_PRED: _Predict4Rd(dst, top, left, topLeft); break;
      case B_VR_PRED: _Predict4Vr(dst, top, left, topLeft); break;
      case B_LD_PRED: _Predict4Ld(dst, top); break;
      case B_VL_PRED: _Predict4Vl(dst, top); break;
      case B_HD_PRED: _Predict4Hd(dst, top, left, topLeft); break;
      case B_HU_PRED: _Predict4Hu(dst, left); break;
      default: throw new ArgumentOutOfRangeException(nameof(mode));
    }
  }

  // ---------- Implementations ----------

  private static void _DcPredict(byte[] dst, int size, byte[]? top, byte[]? left) {
    int dc;
    if (top != null && left != null) {
      var sum = size;
      for (var i = 0; i < size; ++i) sum += top[i];
      for (var j = 0; j < size; ++j) sum += left[j];
      dc = sum / (2 * size);
    } else if (top != null) {
      var sum = size >> 1;
      for (var i = 0; i < size; ++i) sum += top[i];
      dc = sum / size;
    } else if (left != null) {
      var sum = size >> 1;
      for (var j = 0; j < size; ++j) sum += left[j];
      dc = sum / size;
    } else {
      dc = 0x80;
    }
    Array.Fill(dst, (byte)dc);
  }

  private static void _TmPredict(byte[] dst, int size, byte[]? top, byte[]? left, byte topLeft) {
    // TrueMotion: dst[j][i] = clip(top[i] + left[j] - topLeft)
    // Spec defaults: top = 127 if absent; left = 129 if absent; topLeft cascades.
    for (var j = 0; j < size; ++j) {
      var L = left != null ? left[j] : (byte)129;
      for (var i = 0; i < size; ++i) {
        var T = top != null ? top[i] : (byte)127;
        var v = T + L - topLeft;
        dst[j * size + i] = v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
      }
    }
  }

  private static void _VerticalPredict(byte[] dst, int size, byte[]? top) {
    for (var j = 0; j < size; ++j)
      for (var i = 0; i < size; ++i)
        dst[j * size + i] = top != null ? top[i] : (byte)127;
  }

  private static void _HorizontalPredict(byte[] dst, int size, byte[]? left) {
    for (var j = 0; j < size; ++j) {
      var v = left != null ? left[j] : (byte)129;
      for (var i = 0; i < size; ++i)
        dst[j * size + i] = v;
    }
  }

  // --- 4x4 mode-specific predictors ---
  // Samples named per RFC 6386 §12.3 diagram:
  //   a b c d e f g h     (top row; a = topLeft, b..e above block, f..h top-right extension)
  //   p . . . .           (left column)
  //   q . X X X X
  //   r . X X X X
  //   s . X X X X

  private static byte _Avg3(int x, int y, int z) => (byte)(x + 2 * y + z + 2 >> 2);
  private static byte _Avg2(int x, int y) => (byte)(x + y + 1 >> 1);

  private static void _Predict4Dc(byte[] dst, byte[]? top, byte[]? left) {
    int sum = 4;
    if (top != null) for (var i = 0; i < 4; ++i) sum += top[i];
    else sum += 127 * 4;
    if (left != null) for (var j = 0; j < 4; ++j) sum += left[j];
    else sum += 129 * 4;
    var dc = (byte)(sum >> 3);
    Array.Fill(dst, dc);
  }

  private static void _Predict4Tm(byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    for (var j = 0; j < 4; ++j) {
      var L = left != null ? left[j] : (byte)129;
      for (var i = 0; i < 4; ++i) {
        var T = top != null ? top[i] : (byte)127;
        var v = T + L - topLeft;
        dst[j * 4 + i] = v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
      }
    }
  }

  private static void _Predict4Ve(byte[] dst, byte[]? top, byte topLeft) {
    int a = topLeft;
    int b = top != null ? top[0] : 127;
    int c = top != null ? top[1] : 127;
    int d = top != null ? top[2] : 127;
    int e = top != null ? top[3] : 127;
    int f = top != null && top.Length > 4 ? top[4] : e;
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    var cde = _Avg3(c, d, e);
    var def = _Avg3(d, e, f);
    for (var j = 0; j < 4; ++j) {
      dst[j * 4 + 0] = abc;
      dst[j * 4 + 1] = bcd;
      dst[j * 4 + 2] = cde;
      dst[j * 4 + 3] = def;
    }
  }

  private static void _Predict4He(byte[] dst, byte[]? left, byte topLeft) {
    int a = topLeft;
    int p = left != null ? left[0] : 129;
    int q = left != null ? left[1] : 129;
    int r = left != null ? left[2] : 129;
    int s = left != null ? left[3] : 129;
    var apq = _Avg3(a, p, q);
    var rqp = _Avg3(r, q, p);
    var srq = _Avg3(s, r, q);
    var ssr = _Avg3(s, s, r);
    for (var i = 0; i < 4; ++i) {
      dst[0 * 4 + i] = apq;
      dst[1 * 4 + i] = rqp;
      dst[2 * 4 + i] = srq;
      dst[3 * 4 + i] = ssr;
    }
  }

  private static void _Predict4Rd(byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    int s = left != null ? left[3] : 129;
    int r = left != null ? left[2] : 129;
    int q = left != null ? left[1] : 129;
    int p = left != null ? left[0] : 129;
    int a = topLeft;
    int b = top != null ? top[0] : 127;
    int c = top != null ? top[1] : 127;
    int d = top != null ? top[2] : 127;
    int e = top != null ? top[3] : 127;
    var srq = _Avg3(s, r, q);
    var rqp = _Avg3(r, q, p);
    var qpa = _Avg3(q, p, a);
    var pab = _Avg3(p, a, b);
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    var cde = _Avg3(c, d, e);
    dst[0 * 4 + 0] = pab; dst[0 * 4 + 1] = abc; dst[0 * 4 + 2] = bcd; dst[0 * 4 + 3] = cde;
    dst[1 * 4 + 0] = qpa; dst[1 * 4 + 1] = pab; dst[1 * 4 + 2] = abc; dst[1 * 4 + 3] = bcd;
    dst[2 * 4 + 0] = rqp; dst[2 * 4 + 1] = qpa; dst[2 * 4 + 2] = pab; dst[2 * 4 + 3] = abc;
    dst[3 * 4 + 0] = srq; dst[3 * 4 + 1] = rqp; dst[3 * 4 + 2] = qpa; dst[3 * 4 + 3] = pab;
  }

  private static void _Predict4Vr(byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    int r = left != null ? left[2] : 129;
    int q = left != null ? left[1] : 129;
    int p = left != null ? left[0] : 129;
    int a = topLeft;
    int b = top != null ? top[0] : 127;
    int c = top != null ? top[1] : 127;
    int d = top != null ? top[2] : 127;
    int e = top != null ? top[3] : 127;
    var ab = _Avg2(a, b);
    var bc = _Avg2(b, c);
    var cd = _Avg2(c, d);
    var de = _Avg2(d, e);
    var rqp = _Avg3(r, q, p);
    var qpa = _Avg3(q, p, a);
    var pab = _Avg3(p, a, b);
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    var cde = _Avg3(c, d, e);
    dst[0 * 4 + 0] = ab;  dst[0 * 4 + 1] = bc;  dst[0 * 4 + 2] = cd;  dst[0 * 4 + 3] = de;
    dst[1 * 4 + 0] = pab; dst[1 * 4 + 1] = abc; dst[1 * 4 + 2] = bcd; dst[1 * 4 + 3] = cde;
    dst[2 * 4 + 0] = qpa; dst[2 * 4 + 1] = ab;  dst[2 * 4 + 2] = bc;  dst[2 * 4 + 3] = cd;
    dst[3 * 4 + 0] = rqp; dst[3 * 4 + 1] = pab; dst[3 * 4 + 2] = abc; dst[3 * 4 + 3] = bcd;
  }

  private static void _Predict4Ld(byte[] dst, byte[]? top) {
    int a = top != null ? top[0] : 127;
    int b = top != null ? top[1] : 127;
    int c = top != null ? top[2] : 127;
    int d = top != null ? top[3] : 127;
    int e = top != null && top.Length > 4 ? top[4] : d;
    int f = top != null && top.Length > 5 ? top[5] : d;
    int g = top != null && top.Length > 6 ? top[6] : d;
    int h = top != null && top.Length > 7 ? top[7] : d;
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    var cde = _Avg3(c, d, e);
    var def = _Avg3(d, e, f);
    var efg = _Avg3(e, f, g);
    var fgh = _Avg3(f, g, h);
    var ghh = _Avg3(g, h, h);
    dst[0 * 4 + 0] = abc; dst[0 * 4 + 1] = bcd; dst[0 * 4 + 2] = cde; dst[0 * 4 + 3] = def;
    dst[1 * 4 + 0] = bcd; dst[1 * 4 + 1] = cde; dst[1 * 4 + 2] = def; dst[1 * 4 + 3] = efg;
    dst[2 * 4 + 0] = cde; dst[2 * 4 + 1] = def; dst[2 * 4 + 2] = efg; dst[2 * 4 + 3] = fgh;
    dst[3 * 4 + 0] = def; dst[3 * 4 + 1] = efg; dst[3 * 4 + 2] = fgh; dst[3 * 4 + 3] = ghh;
  }

  private static void _Predict4Vl(byte[] dst, byte[]? top) {
    int a = top != null ? top[0] : 127;
    int b = top != null ? top[1] : 127;
    int c = top != null ? top[2] : 127;
    int d = top != null ? top[3] : 127;
    int e = top != null && top.Length > 4 ? top[4] : d;
    int f = top != null && top.Length > 5 ? top[5] : d;
    int g = top != null && top.Length > 6 ? top[6] : d;
    int h = top != null && top.Length > 7 ? top[7] : d;
    var ab = _Avg2(a, b);
    var bc = _Avg2(b, c);
    var cd = _Avg2(c, d);
    var de = _Avg2(d, e);
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    var cde = _Avg3(c, d, e);
    var def = _Avg3(d, e, f);
    var efg = _Avg3(e, f, g);
    var fgh = _Avg3(f, g, h);
    dst[0 * 4 + 0] = ab;  dst[0 * 4 + 1] = bc;  dst[0 * 4 + 2] = cd;  dst[0 * 4 + 3] = de;
    dst[1 * 4 + 0] = abc; dst[1 * 4 + 1] = bcd; dst[1 * 4 + 2] = cde; dst[1 * 4 + 3] = def;
    dst[2 * 4 + 0] = bc;  dst[2 * 4 + 1] = cd;  dst[2 * 4 + 2] = de;  dst[2 * 4 + 3] = efg;
    dst[3 * 4 + 0] = bcd; dst[3 * 4 + 1] = cde; dst[3 * 4 + 2] = def; dst[3 * 4 + 3] = fgh;
  }

  private static void _Predict4Hd(byte[] dst, byte[]? top, byte[]? left, byte topLeft) {
    int s = left != null ? left[3] : 129;
    int r = left != null ? left[2] : 129;
    int q = left != null ? left[1] : 129;
    int p = left != null ? left[0] : 129;
    int a = topLeft;
    int b = top != null ? top[0] : 127;
    int c = top != null ? top[1] : 127;
    int d = top != null ? top[2] : 127;
    var sr = _Avg2(s, r);
    var rq = _Avg2(r, q);
    var qp = _Avg2(q, p);
    var pa = _Avg2(p, a);
    var srq = _Avg3(s, r, q);
    var rqp = _Avg3(r, q, p);
    var qpa = _Avg3(q, p, a);
    var pab = _Avg3(p, a, b);
    var abc = _Avg3(a, b, c);
    var bcd = _Avg3(b, c, d);
    dst[0 * 4 + 0] = pa;  dst[0 * 4 + 1] = pab; dst[0 * 4 + 2] = abc; dst[0 * 4 + 3] = bcd;
    dst[1 * 4 + 0] = qp;  dst[1 * 4 + 1] = qpa; dst[1 * 4 + 2] = pa;  dst[1 * 4 + 3] = pab;
    dst[2 * 4 + 0] = rq;  dst[2 * 4 + 1] = rqp; dst[2 * 4 + 2] = qp;  dst[2 * 4 + 3] = qpa;
    dst[3 * 4 + 0] = sr;  dst[3 * 4 + 1] = srq; dst[3 * 4 + 2] = rq;  dst[3 * 4 + 3] = rqp;
  }

  private static void _Predict4Hu(byte[] dst, byte[]? left) {
    int s = left != null ? left[3] : 129;
    int r = left != null ? left[2] : 129;
    int q = left != null ? left[1] : 129;
    int p = left != null ? left[0] : 129;
    var pq = _Avg2(p, q);
    var qr = _Avg2(q, r);
    var rs = _Avg2(r, s);
    var pqr = _Avg3(p, q, r);
    var qrs = _Avg3(q, r, s);
    var rss = _Avg3(r, s, s);
    var sss = (byte)s;
    dst[0 * 4 + 0] = pq;  dst[0 * 4 + 1] = pqr; dst[0 * 4 + 2] = qr;  dst[0 * 4 + 3] = qrs;
    dst[1 * 4 + 0] = qr;  dst[1 * 4 + 1] = qrs; dst[1 * 4 + 2] = rs;  dst[1 * 4 + 3] = rss;
    dst[2 * 4 + 0] = rs;  dst[2 * 4 + 1] = rss; dst[2 * 4 + 2] = sss; dst[2 * 4 + 3] = sss;
    dst[3 * 4 + 0] = sss; dst[3 * 4 + 1] = sss; dst[3 * 4 + 2] = sss; dst[3 * 4 + 3] = sss;
  }
}
