using System;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// Lifting steps of the inverse photo core transform and the inverse overlap (post) filters
/// (T.832 clause 9.8-9.10, jxrlib strTransform.c / strInvTransform.c). Every operand is an index into one working array.
/// </summary>
internal static class JxrTransform {

  /// <summary>2x2 Hadamard; used as both forward and inverse (operands b and c swapped by the caller).</summary>
  internal static void DCT2x2dn(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var cc = m[pc];
    var d = m[pd];
    a += d;
    b -= cc;
    var t = (a - b) >> 1;
    var c = t - d;
    d = t - cc;
    a -= d;
    b += c;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  internal static void DCT2x2up(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var cc = m[pc];
    var d = m[pd];
    a += d;
    b -= cc;
    var t = (a - b + 1) >> 1;
    var c = t - d;
    d = t - cc;
    a -= d;
    b += c;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  /// <summary>2x2 Hadamard followed by the x2 rescale needed by the scaled-arithmetic chroma path.</summary>
  internal static void DCT2x2dnDec(int[] m, int pa, int pb, int pc, int pd) {
    DCT2x2dn(m, pa, pb, pc, pd);
    m[pa] *= 2;
    m[pb] *= 2;
    m[pc] *= 2;
    m[pd] *= 2;
  }

  private static void _FourButterfly(int[] m, int p, ReadOnlySpan<int> o) {
    DCT2x2dn(m, p + o[0], p + o[1], p + o[2], p + o[3]);
    DCT2x2dn(m, p + o[4], p + o[5], p + o[6], p + o[7]);
    DCT2x2dn(m, p + o[8], p + o[9], p + o[10], p + o[11]);
    DCT2x2dn(m, p + o[12], p + o[13], p + o[14], p + o[15]);
  }

  /// <summary>rotation by -pi/8</summary>
  private static void _IRotate1(int[] m, int pa, int pb) {
    m[pa] -= (m[pb] + 1) >> 1;
    m[pb] += (m[pa] + 1) >> 1;
  }

  private static void _IRotate2(ref int a, ref int b) {
    a -= (b * 3 + 4) >> 3;
    b += (a * 3 + 4) >> 3;
  }

  /// <summary>Kron(Rotate(-pi/8), [1 1; 1 -1]/sqrt(2)).</summary>
  private static void _InvOdd(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    b += d;
    a -= c;
    d -= b >> 1;
    c += (a + 1) >> 1;
    _IRotate2(ref a, ref b);
    _IRotate2(ref c, ref d);
    c -= (b + 1) >> 1;
    d = ((a + 1) >> 1) - d;
    b += c;
    a -= d;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  /// <summary>Kron(Rotate(pi/8), Rotate(pi/8)).</summary>
  private static void _InvOddOdd(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    d += a;
    c -= b;
    int t1, t2;
    a -= t1 = d >> 1;
    b += t2 = c >> 1;
    a -= (b * 3 + 3) >> 3;
    b += (a * 3 + 3) >> 2;
    a -= (b * 3 + 4) >> 3;
    b -= t2;
    a += t1;
    c += b;
    d -= a;
    m[pa] = a;
    m[pb] = -b;
    m[pc] = -c;
    m[pd] = d;
  }

  private static void _InvOddOddPost(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    d += a;
    c -= b;
    int t1, t2;
    a -= t1 = d >> 1;
    b += t2 = c >> 1;
    a -= (b * 3 + 6) >> 3;
    b += (a * 3 + 2) >> 2;
    a -= (b * 3 + 4) >> 3;
    b -= t2;
    a += t1;
    c += b;
    d -= a;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  private static readonly int[] _Butterfly1 = [0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15];
  private static readonly int[] _Butterfly2 = [0, 192, 48, 240, 64, 128, 112, 176, 16, 208, 32, 224, 80, 144, 96, 160];

  /// <summary>First stage 4x4 inverse core transform of one block stored contiguously.</summary>
  internal static void IDCT4x4Stage1(int[] m, int p) {
    DCT2x2up(m, p + 0, p + 1, p + 2, p + 3);
    _InvOdd(m, p + 5, p + 4, p + 7, p + 6);
    _InvOdd(m, p + 10, p + 8, p + 11, p + 9);
    _InvOddOdd(m, p + 15, p + 14, p + 13, p + 12);
    _FourButterfly(m, p, _Butterfly1);
  }

  /// <summary>Second stage 4x4 inverse core transform over the 16 block DCs of a macroblock.</summary>
  internal static void IDCT4x4Stage2(int[] m, int p) {
    _InvOdd(m, p + 32, p + 48, p + 96, p + 112);
    _InvOdd(m, p + 128, p + 192, p + 144, p + 208);
    _InvOddOdd(m, p + 160, p + 224, p + 176, p + 240);
    DCT2x2up(m, p + 0, p + 64, p + 16, p + 80);
    _FourButterfly(m, p, _Butterfly2);
  }

  internal static void NormalizeDec(int[] m, int p, bool chroma) {
    if (!chroma)
      return;
    for (var i = 0; i < 256; i += 16)
      m[p + i] += m[p + i];
  }

  internal static void Post2(int[] m, int pa, int pb) {
    m[pb] += (m[pa] + 4) >> 3;
    m[pa] += (m[pb] + 2) >> 2;
    m[pb] += (m[pa] + 4) >> 3;
  }

  internal static void Post2Alternate(int[] m, int pa, int pb) {
    var a = m[pa];
    var b = m[pb];
    b += (a + 2) >> 2;
    a += (b + 1) >> 1;
    a += b >> 5;
    a += b >> 9;
    a += b >> 13;
    b += (a + 2) >> 2;
    m[pa] = a;
    m[pb] = b;
  }

  internal static void Post2x2(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    a += d;
    b += c;
    d -= (a + 1) >> 1;
    c -= (b + 1) >> 1;
    b += (a + 2) >> 2;
    a += (b + 1) >> 1;
    b += (a + 2) >> 2;
    d += (a + 1) >> 1;
    c += (b + 1) >> 1;
    a -= d;
    b -= c;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  internal static void Post2x2Alternate(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    a += d;
    b += c;
    d -= (a + 1) >> 1;
    c -= (b + 1) >> 1;
    b += (a + 2) >> 2;
    a += (b + 1) >> 1;
    a += b >> 5;
    a += b >> 9;
    a += b >> 13;
    b += (a + 2) >> 2;
    d += (a + 1) >> 1;
    c += (b + 1) >> 1;
    a -= d;
    b -= c;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  /// <summary>4-point post filter used along image and tile edges.</summary>
  internal static void Post4(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    a += d;
    b += c;
    d -= (a + 1) >> 1;
    c -= (b + 1) >> 1;
    c -= (d + 1) >> 1;
    d += (c + 1) >> 1;
    d += (a + 1) >> 1;
    c += (b + 1) >> 1;
    a -= d - ((d * 3 + 16) >> 5);
    b -= c - ((c * 3 + 16) >> 5);
    d += (a * 3 + 8) >> 4;
    c += (b * 3 + 8) >> 4;
    a += (d * 3 + 16) >> 5;
    b += (c * 3 + 16) >> 5;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  internal static void Post4Alternate(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    a += d;
    b += c;
    d -= (a + 1) >> 1;
    c -= (b + 1) >> 1;
    _HSTdec1Edge(ref a, ref d);
    _HSTdec1Edge(ref b, ref c);
    c -= (d + 1) >> 1;
    d += (c + 1) >> 1;
    d += (a + 1) >> 1;
    c += (b + 1) >> 1;
    a -= d;
    b -= c;
    m[pa] = a;
    m[pb] = b;
    m[pc] = c;
    m[pd] = d;
  }

  private static void _HSTdec1(int[] m, int pa, int pd) {
    var a = m[pa];
    var d = m[pd];
    a += d;
    d = (a >> 1) - d;
    a += (d * 3 + 0) >> 3;
    d += (a * 3 + 0) >> 4;
    m[pa] = a;
    m[pd] = d;
  }

  private static void _HSTdec1Alternate(int[] m, int pa, int pd) {
    var a = m[pa];
    var d = m[pd];
    a += d;
    d = (a >> 1) - d;
    a += (d * 3 + 0) >> 3;
    d += (a * 3 + 0) >> 4;
    d += a >> 7;
    d -= a >> 10;
    m[pa] = a;
    m[pd] = d;
  }

  /// <summary>1D scaling operator of the altered-operator edge filter (adds 7/1024 in two steps).</summary>
  private static void _HSTdec1Edge(ref int pa, ref int pd) {
    var a = pa;
    var d = pd;
    a += d;
    d = (a >> 1) - d;
    a += (d * 3 + 0) >> 3;
    d += (a * 3 + 0) >> 4;
    d += a >> 7;
    d -= a >> 10;
    a += (d * 3 + 4) >> 3;
    d -= a >> 1;
    a += d;
    pa = a;
    pd = -d;
  }

  private static void _HSTdec(int[] m, int pa, int pb, int pc, int pd) {
    var a = m[pa];
    var b = m[pb];
    var c = m[pc];
    var d = m[pd];
    b -= c;
    a += (d * 3 + 4) >> 3;
    d -= b >> 1;
    c = ((a - b) >> 1) - c;
    m[pc] = d;
    m[pd] = c;
    m[pa] = a - c;
    m[pb] = b + d;
  }

  private static void _DCCompensate(int[] m, int pa, int pb, int pc, int pd, int dc) {
    dc >>= 1;
    m[pa] -= dc;
    m[pd] -= dc;
    m[pb] += dc;
    m[pc] += dc;
  }

  private static int _ClipDCL(int dcl, int altDcl) {
    if (dcl > 0)
      return altDcl > 0 ? Math.Min(dcl, altDcl) : 0;
    if (dcl < 0)
      return altDcl < 0 ? Math.Max(dcl, altDcl) : 0;
    return 0;
  }

  /// <summary>4x4 first stage post filter straddling four blocks, with the low-rate DC leakage compensation.</summary>
  internal static void Post4x4Stage1Split(int[] m, int p0, int p1, int offset, int hpQP, bool hpAbsent) {
    var p2 = p0 + 72 - offset;
    var p3 = p1 + 64 - offset;
    p0 += 12;
    p1 += 4;

    for (var i = 0; i < 4; ++i)
      DCT2x2dn(m, p0 + i, p2 + i, p1 + i, p3 + i);

    _InvOddOddPost(m, p3 + 0, p3 + 1, p3 + 2, p3 + 3);

    _IRotate1(m, p1 + 2, p1 + 3);
    _IRotate1(m, p1 + 0, p1 + 1);
    _IRotate1(m, p2 + 1, p2 + 3);
    _IRotate1(m, p2 + 0, p2 + 2);

    for (var i = 0; i < 4; ++i)
      _HSTdec1(m, p0 + i, p3 + i);
    for (var i = 0; i < 4; ++i)
      _HSTdec(m, p0 + i, p2 + i, p1 + i, p3 + i);

    Span<int> dcl = stackalloc int[4];
    for (var i = 0; i < 4; ++i) {
      var tmp = (m[p0 + i] + m[p1 + i] + m[p2 + i] + m[p3 + i]) >> 1;
      dcl[i] = (tmp * 595 + 65536) >> 17;
    }

    for (var i = 0; i < 4; ++i) {
      if (!((Math.Abs(dcl[i]) < hpQP && hpQP > 20) || hpAbsent))
        continue;
      var alt = (m[p0 + i] - m[p1 + i] - m[p2 + i] + m[p3 + i]) >> 1;
      var clipped = _ClipDCL(dcl[i], alt);
      _DCCompensate(m, p0 + i, p2 + i, p1 + i, p3 + i, clipped);
    }
  }

  internal static void Post4x4Stage1(int[] m, int p, int offset, int hpQP, bool hpAbsent)
    => Post4x4Stage1Split(m, p, p + 16, offset, hpQP, hpAbsent);

  internal static void Post4x4Stage1SplitAlternate(int[] m, int p0, int p1, int offset) {
    var p2 = p0 + 72 - offset;
    var p3 = p1 + 64 - offset;
    p0 += 12;
    p1 += 4;

    for (var i = 0; i < 4; ++i)
      DCT2x2dn(m, p0 + i, p2 + i, p1 + i, p3 + i);

    _InvOddOddPost(m, p3 + 0, p3 + 1, p3 + 2, p3 + 3);

    _IRotate1(m, p1 + 2, p1 + 3);
    _IRotate1(m, p1 + 0, p1 + 1);
    _IRotate1(m, p2 + 1, p2 + 3);
    _IRotate1(m, p2 + 0, p2 + 2);

    for (var i = 0; i < 4; ++i)
      _HSTdec1Alternate(m, p0 + i, p3 + i);
    for (var i = 0; i < 4; ++i)
      _HSTdec(m, p0 + i, p2 + i, p1 + i, p3 + i);
  }

  internal static void Post4x4Stage1Alternate(int[] m, int p, int offset)
    => Post4x4Stage1SplitAlternate(m, p, p + 16, offset);

  /// <summary>Second stage 4x4 post filter across the macroblock corner (offsets relative to the previous and current row).</summary>
  internal static void Post4x4Stage2Split(int[] m, int p0, int p1) => _Post4x4Stage2(m, p0, p1, false);

  internal static void Post4x4Stage2SplitAlternate(int[] m, int p0, int p1) => _Post4x4Stage2(m, p0, p1, true);

  private static void _Post4x4Stage2(int[] m, int p0, int p1, bool alternate) {
    DCT2x2dn(m, p0 - 96, p0 + 96, p1 - 112, p1 + 80);
    DCT2x2dn(m, p0 - 32, p0 + 32, p1 - 48, p1 + 16);
    DCT2x2dn(m, p0 - 80, p0 + 112, p1 - 128, p1 + 64);
    DCT2x2dn(m, p0 - 16, p0 + 48, p1 - 64, p1 + 0);

    _InvOddOddPost(m, p1 + 0, p1 + 64, p1 + 16, p1 + 80);

    _IRotate1(m, p0 + 48, p0 + 32);
    _IRotate1(m, p0 + 112, p0 + 96);
    _IRotate1(m, p1 - 64, p1 - 128);
    _IRotate1(m, p1 - 48, p1 - 112);

    if (alternate) {
      _HSTdec1Alternate(m, p0 - 96, p1 + 80);
      _HSTdec1Alternate(m, p0 - 32, p1 + 16);
      _HSTdec1Alternate(m, p0 - 80, p1 + 64);
      _HSTdec1Alternate(m, p0 - 16, p1 + 0);
    } else {
      _HSTdec1(m, p0 - 96, p1 + 80);
      _HSTdec1(m, p0 - 32, p1 + 16);
      _HSTdec1(m, p0 - 80, p1 + 64);
      _HSTdec1(m, p0 - 16, p1 + 0);
    }

    _HSTdec(m, p0 - 96, p1 - 112, p0 + 96, p1 + 80);
    _HSTdec(m, p0 - 32, p1 - 48, p0 + 32, p1 + 16);
    _HSTdec(m, p0 - 80, p1 - 128, p0 + 112, p1 + 64);
    _HSTdec(m, p0 - 16, p1 - 64, p0 + 48, p1 + 0);
  }
}
