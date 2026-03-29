using System;

namespace FileFormat.WebP.Vp8;

/// <summary>VP8 intra prediction modes for 4x4 and 16x16 luma blocks, and 8x8 chroma blocks.</summary>
internal static class Vp8IntraPredictor {

  // 16x16 prediction mode indices
  public const int DC_PRED = 0;
  public const int V_PRED = 1;
  public const int H_PRED = 2;
  public const int TM_PRED = 3;

  // 4x4 prediction mode indices
  public const int B_DC_PRED = 0;
  public const int B_TM_PRED = 1;
  public const int B_VE_PRED = 2;
  public const int B_HE_PRED = 3;
  public const int B_RD_PRED = 4;
  public const int B_VR_PRED = 5;
  public const int B_LD_PRED = 6;
  public const int B_VL_PRED = 7;
  public const int B_HD_PRED = 8;
  public const int B_HU_PRED = 9;

  /// <summary>16x16 luma intra prediction.</summary>
  public static void Predict16x16(int mode, byte[] dst, int offset, int stride, byte[]? above, byte[]? left, byte topLeft) {
    switch (mode) {
      case DC_PRED:
        _Predict16x16Dc(dst, offset, stride, above, left);
        break;
      case V_PRED:
        _Predict16x16V(dst, offset, stride, above!);
        break;
      case H_PRED:
        _Predict16x16H(dst, offset, stride, left!);
        break;
      case TM_PRED:
        _Predict16x16Tm(dst, offset, stride, above!, left!, topLeft);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid 16x16 prediction mode.");
    }
  }

  /// <summary>8x8 chroma intra prediction (same 4 modes as 16x16).</summary>
  public static void Predict8x8(int mode, byte[] dst, int offset, int stride, byte[]? above, byte[]? left, byte topLeft) {
    switch (mode) {
      case DC_PRED:
        _Predict8x8Dc(dst, offset, stride, above, left);
        break;
      case V_PRED:
        _Predict8x8V(dst, offset, stride, above!);
        break;
      case H_PRED:
        _Predict8x8H(dst, offset, stride, left!);
        break;
      case TM_PRED:
        _Predict8x8Tm(dst, offset, stride, above!, left!, topLeft);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid 8x8 prediction mode.");
    }
  }

  /// <summary>4x4 luma intra prediction (10 modes).</summary>
  public static void Predict4x4(int mode, byte[] dst, int offset, int stride, byte[]? above, int aboveOffset, byte[]? left, byte topLeft) {
    switch (mode) {
      case B_DC_PRED:
        _Predict4x4Dc(dst, offset, stride, above, aboveOffset, left);
        break;
      case B_TM_PRED:
        _Predict4x4Tm(dst, offset, stride, above!, aboveOffset, left!, topLeft);
        break;
      case B_VE_PRED:
        _Predict4x4Ve(dst, offset, stride, above!, aboveOffset, topLeft);
        break;
      case B_HE_PRED:
        _Predict4x4He(dst, offset, stride, left!, topLeft);
        break;
      case B_RD_PRED:
        _Predict4x4Rd(dst, offset, stride, above!, aboveOffset, left!, topLeft);
        break;
      case B_VR_PRED:
        _Predict4x4Vr(dst, offset, stride, above!, aboveOffset, left!, topLeft);
        break;
      case B_LD_PRED:
        _Predict4x4Ld(dst, offset, stride, above!, aboveOffset);
        break;
      case B_VL_PRED:
        _Predict4x4Vl(dst, offset, stride, above!, aboveOffset);
        break;
      case B_HD_PRED:
        _Predict4x4Hd(dst, offset, stride, above!, aboveOffset, left!, topLeft);
        break;
      case B_HU_PRED:
        _Predict4x4Hu(dst, offset, stride, left!);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid 4x4 prediction mode.");
    }
  }

  #region 16x16 modes

  private static void _Predict16x16Dc(byte[] dst, int offset, int stride, byte[]? above, byte[]? left) {
    var sum = 0;
    var count = 0;
    if (above != null) {
      for (var i = 0; i < 16; ++i)
        sum += above[i];
      count += 16;
    }

    if (left != null) {
      for (var i = 0; i < 16; ++i)
        sum += left[i];
      count += 16;
    }

    var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;
    for (var row = 0; row < 16; ++row) {
      var off = offset + row * stride;
      for (var col = 0; col < 16; ++col)
        dst[off + col] = dc;
    }
  }

  private static void _Predict16x16V(byte[] dst, int offset, int stride, byte[] above) {
    for (var row = 0; row < 16; ++row) {
      var off = offset + row * stride;
      Buffer.BlockCopy(above, 0, dst, off, 16);
    }
  }

  private static void _Predict16x16H(byte[] dst, int offset, int stride, byte[] left) {
    for (var row = 0; row < 16; ++row) {
      var off = offset + row * stride;
      var val = left[row];
      for (var col = 0; col < 16; ++col)
        dst[off + col] = val;
    }
  }

  private static void _Predict16x16Tm(byte[] dst, int offset, int stride, byte[] above, byte[] left, byte topLeft) {
    for (var row = 0; row < 16; ++row) {
      var off = offset + row * stride;
      var l = left[row];
      for (var col = 0; col < 16; ++col)
        dst[off + col] = _Clamp(l + above[col] - topLeft);
    }
  }

  #endregion

  #region 8x8 modes

  private static void _Predict8x8Dc(byte[] dst, int offset, int stride, byte[]? above, byte[]? left) {
    var sum = 0;
    var count = 0;
    if (above != null) {
      for (var i = 0; i < 8; ++i)
        sum += above[i];
      count += 8;
    }

    if (left != null) {
      for (var i = 0; i < 8; ++i)
        sum += left[i];
      count += 8;
    }

    var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;
    for (var row = 0; row < 8; ++row) {
      var off = offset + row * stride;
      for (var col = 0; col < 8; ++col)
        dst[off + col] = dc;
    }
  }

  private static void _Predict8x8V(byte[] dst, int offset, int stride, byte[] above) {
    for (var row = 0; row < 8; ++row) {
      var off = offset + row * stride;
      Buffer.BlockCopy(above, 0, dst, off, 8);
    }
  }

  private static void _Predict8x8H(byte[] dst, int offset, int stride, byte[] left) {
    for (var row = 0; row < 8; ++row) {
      var off = offset + row * stride;
      var val = left[row];
      for (var col = 0; col < 8; ++col)
        dst[off + col] = val;
    }
  }

  private static void _Predict8x8Tm(byte[] dst, int offset, int stride, byte[] above, byte[] left, byte topLeft) {
    for (var row = 0; row < 8; ++row) {
      var off = offset + row * stride;
      var l = left[row];
      for (var col = 0; col < 8; ++col)
        dst[off + col] = _Clamp(l + above[col] - topLeft);
    }
  }

  #endregion

  #region 4x4 modes

  private static void _Predict4x4Dc(byte[] dst, int offset, int stride, byte[]? above, int aboveOffset, byte[]? left) {
    var sum = 0;
    var count = 0;
    if (above != null) {
      for (var i = 0; i < 4; ++i)
        sum += above[aboveOffset + i];
      count += 4;
    }

    if (left != null) {
      for (var i = 0; i < 4; ++i)
        sum += left[i];
      count += 4;
    }

    var dc = count > 0 ? (byte)((sum + (count >> 1)) / count) : (byte)128;
    for (var row = 0; row < 4; ++row) {
      var off = offset + row * stride;
      dst[off + 0] = dc;
      dst[off + 1] = dc;
      dst[off + 2] = dc;
      dst[off + 3] = dc;
    }
  }

  private static void _Predict4x4Tm(byte[] dst, int offset, int stride, byte[] above, int aboveOffset, byte[] left, byte topLeft) {
    for (var row = 0; row < 4; ++row) {
      var off = offset + row * stride;
      var l = left[row];
      for (var col = 0; col < 4; ++col)
        dst[off + col] = _Clamp(l + above[aboveOffset + col] - topLeft);
    }
  }

  // VE: vertical with smoothing (avg3 of above neighbors)
  private static void _Predict4x4Ve(byte[] dst, int offset, int stride, byte[] above, int aboveOffset, byte topLeft) {
    // above[-1]=topLeft, above[0..3] are the top pixels, above[4] is the top-right pixel
    var a = aboveOffset;
    var p0 = _Avg3(topLeft, above[a + 0], above[a + 1]);
    var p1 = _Avg3(above[a + 0], above[a + 1], above[a + 2]);
    var p2 = _Avg3(above[a + 1], above[a + 2], above[a + 3]);
    // For above[a+4], use above[a+3] if out of bounds
    var right = a + 4 < above.Length ? above[a + 4] : above[a + 3];
    var p3 = _Avg3(above[a + 2], above[a + 3], right);

    for (var row = 0; row < 4; ++row) {
      var off = offset + row * stride;
      dst[off + 0] = p0;
      dst[off + 1] = p1;
      dst[off + 2] = p2;
      dst[off + 3] = p3;
    }
  }

  // HE: horizontal with smoothing (avg3 of left neighbors)
  private static void _Predict4x4He(byte[] dst, int offset, int stride, byte[] left, byte topLeft) {
    var p0 = _Avg3(topLeft, left[0], left[1]);
    var p1 = _Avg3(left[0], left[1], left[2]);
    var p2 = _Avg3(left[1], left[2], left[3]);
    var p3 = _Avg3(left[2], left[3], left[3]);

    _Fill4(dst, offset + 0 * stride, p0);
    _Fill4(dst, offset + 1 * stride, p1);
    _Fill4(dst, offset + 2 * stride, p2);
    _Fill4(dst, offset + 3 * stride, p3);
  }

  // RD: right-down diagonal
  private static void _Predict4x4Rd(byte[] dst, int offset, int stride, byte[] above, int aboveOffset, byte[] left, byte topLeft) {
    var a = aboveOffset;
    var x = topLeft;
    var a0 = above[a + 0];
    var a1 = above[a + 1];
    var a2 = above[a + 2];
    var a3 = above[a + 3];
    var l0 = left[0];
    var l1 = left[1];
    var l2 = left[2];
    var l3 = left[3];

    // Build the 7 interpolated values for the diagonal
    var d0 = _Avg3(a3, a2, a1);
    var d1 = _Avg3(a2, a1, a0);
    var d2 = _Avg3(a1, a0, x);
    var d3 = _Avg3(a0, x, l0);
    var d4 = _Avg3(x, l0, l1);
    var d5 = _Avg3(l0, l1, l2);
    var d6 = _Avg3(l1, l2, l3);

    dst[offset + 0 * stride + 0] = d3;
    dst[offset + 0 * stride + 1] = d2;
    dst[offset + 0 * stride + 2] = d1;
    dst[offset + 0 * stride + 3] = d0;
    dst[offset + 1 * stride + 0] = d4;
    dst[offset + 1 * stride + 1] = d3;
    dst[offset + 1 * stride + 2] = d2;
    dst[offset + 1 * stride + 3] = d1;
    dst[offset + 2 * stride + 0] = d5;
    dst[offset + 2 * stride + 1] = d4;
    dst[offset + 2 * stride + 2] = d3;
    dst[offset + 2 * stride + 3] = d2;
    dst[offset + 3 * stride + 0] = d6;
    dst[offset + 3 * stride + 1] = d5;
    dst[offset + 3 * stride + 2] = d4;
    dst[offset + 3 * stride + 3] = d3;
  }

  // VR: vertical-right diagonal
  private static void _Predict4x4Vr(byte[] dst, int offset, int stride, byte[] above, int aboveOffset, byte[] left, byte topLeft) {
    var a = aboveOffset;
    var x = topLeft;
    var a0 = above[a + 0];
    var a1 = above[a + 1];
    var a2 = above[a + 2];
    var a3 = above[a + 3];
    var l0 = left[0];
    var l1 = left[1];
    var l2 = left[2];

    // Even rows use avg2, odd rows use avg3
    dst[offset + 0 * stride + 0] = _Avg2(x, a0);
    dst[offset + 0 * stride + 1] = _Avg2(a0, a1);
    dst[offset + 0 * stride + 2] = _Avg2(a1, a2);
    dst[offset + 0 * stride + 3] = _Avg2(a2, a3);
    dst[offset + 1 * stride + 0] = _Avg3(l0, x, a0);
    dst[offset + 1 * stride + 1] = _Avg3(x, a0, a1);
    dst[offset + 1 * stride + 2] = _Avg3(a0, a1, a2);
    dst[offset + 1 * stride + 3] = _Avg3(a1, a2, a3);
    dst[offset + 2 * stride + 0] = _Avg3(l1, l0, x);
    dst[offset + 2 * stride + 1] = _Avg2(x, a0);
    dst[offset + 2 * stride + 2] = _Avg2(a0, a1);
    dst[offset + 2 * stride + 3] = _Avg2(a1, a2);
    dst[offset + 3 * stride + 0] = _Avg3(l2, l1, l0);
    dst[offset + 3 * stride + 1] = _Avg3(l0, x, a0);
    dst[offset + 3 * stride + 2] = _Avg3(x, a0, a1);
    dst[offset + 3 * stride + 3] = _Avg3(a0, a1, a2);
  }

  // LD: left-down diagonal
  private static void _Predict4x4Ld(byte[] dst, int offset, int stride, byte[] above, int aboveOffset) {
    var a = aboveOffset;
    var a0 = above[a + 0];
    var a1 = above[a + 1];
    var a2 = above[a + 2];
    var a3 = above[a + 3];
    var a4 = a + 4 < above.Length ? above[a + 4] : a3;
    var a5 = a + 5 < above.Length ? above[a + 5] : a4;
    var a6 = a + 6 < above.Length ? above[a + 6] : a5;
    var a7 = a + 7 < above.Length ? above[a + 7] : a6;

    dst[offset + 0 * stride + 0] = _Avg3(a0, a1, a2);
    dst[offset + 0 * stride + 1] = _Avg3(a1, a2, a3);
    dst[offset + 0 * stride + 2] = _Avg3(a2, a3, a4);
    dst[offset + 0 * stride + 3] = _Avg3(a3, a4, a5);
    dst[offset + 1 * stride + 0] = _Avg3(a1, a2, a3);
    dst[offset + 1 * stride + 1] = _Avg3(a2, a3, a4);
    dst[offset + 1 * stride + 2] = _Avg3(a3, a4, a5);
    dst[offset + 1 * stride + 3] = _Avg3(a4, a5, a6);
    dst[offset + 2 * stride + 0] = _Avg3(a2, a3, a4);
    dst[offset + 2 * stride + 1] = _Avg3(a3, a4, a5);
    dst[offset + 2 * stride + 2] = _Avg3(a4, a5, a6);
    dst[offset + 2 * stride + 3] = _Avg3(a5, a6, a7);
    dst[offset + 3 * stride + 0] = _Avg3(a3, a4, a5);
    dst[offset + 3 * stride + 1] = _Avg3(a4, a5, a6);
    dst[offset + 3 * stride + 2] = _Avg3(a5, a6, a7);
    dst[offset + 3 * stride + 3] = _Avg3(a6, a7, a7);
  }

  // VL: vertical-left diagonal
  private static void _Predict4x4Vl(byte[] dst, int offset, int stride, byte[] above, int aboveOffset) {
    var a = aboveOffset;
    var a0 = above[a + 0];
    var a1 = above[a + 1];
    var a2 = above[a + 2];
    var a3 = above[a + 3];
    var a4 = a + 4 < above.Length ? above[a + 4] : a3;
    var a5 = a + 5 < above.Length ? above[a + 5] : a4;
    var a6 = a + 6 < above.Length ? above[a + 6] : a5;

    dst[offset + 0 * stride + 0] = _Avg2(a0, a1);
    dst[offset + 0 * stride + 1] = _Avg2(a1, a2);
    dst[offset + 0 * stride + 2] = _Avg2(a2, a3);
    dst[offset + 0 * stride + 3] = _Avg2(a3, a4);
    dst[offset + 1 * stride + 0] = _Avg3(a0, a1, a2);
    dst[offset + 1 * stride + 1] = _Avg3(a1, a2, a3);
    dst[offset + 1 * stride + 2] = _Avg3(a2, a3, a4);
    dst[offset + 1 * stride + 3] = _Avg3(a3, a4, a5);
    dst[offset + 2 * stride + 0] = _Avg2(a1, a2);
    dst[offset + 2 * stride + 1] = _Avg2(a2, a3);
    dst[offset + 2 * stride + 2] = _Avg2(a3, a4);
    dst[offset + 2 * stride + 3] = _Avg2(a4, a5);
    dst[offset + 3 * stride + 0] = _Avg3(a1, a2, a3);
    dst[offset + 3 * stride + 1] = _Avg3(a2, a3, a4);
    dst[offset + 3 * stride + 2] = _Avg3(a3, a4, a5);
    dst[offset + 3 * stride + 3] = _Avg3(a4, a5, a6);
  }

  // HD: horizontal-down diagonal
  private static void _Predict4x4Hd(byte[] dst, int offset, int stride, byte[] above, int aboveOffset, byte[] left, byte topLeft) {
    var a = aboveOffset;
    var x = topLeft;
    var a0 = above[a + 0];
    var a1 = above[a + 1];
    var a2 = above[a + 2];
    var l0 = left[0];
    var l1 = left[1];
    var l2 = left[2];
    var l3 = left[3];

    dst[offset + 0 * stride + 0] = _Avg2(x, l0);
    dst[offset + 0 * stride + 1] = _Avg3(l0, x, a0);
    dst[offset + 0 * stride + 2] = _Avg3(x, a0, a1);
    dst[offset + 0 * stride + 3] = _Avg3(a0, a1, a2);
    dst[offset + 1 * stride + 0] = _Avg2(l0, l1);
    dst[offset + 1 * stride + 1] = _Avg3(l1, l0, x);
    dst[offset + 1 * stride + 2] = _Avg2(x, l0);
    dst[offset + 1 * stride + 3] = _Avg3(l0, x, a0);
    dst[offset + 2 * stride + 0] = _Avg2(l1, l2);
    dst[offset + 2 * stride + 1] = _Avg3(l2, l1, l0);
    dst[offset + 2 * stride + 2] = _Avg2(l0, l1);
    dst[offset + 2 * stride + 3] = _Avg3(l1, l0, x);
    dst[offset + 3 * stride + 0] = _Avg2(l2, l3);
    dst[offset + 3 * stride + 1] = _Avg3(l3, l2, l1);
    dst[offset + 3 * stride + 2] = _Avg2(l1, l2);
    dst[offset + 3 * stride + 3] = _Avg3(l2, l1, l0);
  }

  // HU: horizontal-up diagonal
  private static void _Predict4x4Hu(byte[] dst, int offset, int stride, byte[] left) {
    var l0 = left[0];
    var l1 = left[1];
    var l2 = left[2];
    var l3 = left[3];

    dst[offset + 0 * stride + 0] = _Avg2(l0, l1);
    dst[offset + 0 * stride + 1] = _Avg3(l0, l1, l2);
    dst[offset + 0 * stride + 2] = _Avg2(l1, l2);
    dst[offset + 0 * stride + 3] = _Avg3(l1, l2, l3);
    dst[offset + 1 * stride + 0] = _Avg2(l1, l2);
    dst[offset + 1 * stride + 1] = _Avg3(l1, l2, l3);
    dst[offset + 1 * stride + 2] = _Avg2(l2, l3);
    dst[offset + 1 * stride + 3] = _Avg3(l2, l3, l3);
    dst[offset + 2 * stride + 0] = _Avg2(l2, l3);
    dst[offset + 2 * stride + 1] = _Avg3(l2, l3, l3);
    dst[offset + 2 * stride + 2] = l3;
    dst[offset + 2 * stride + 3] = l3;
    dst[offset + 3 * stride + 0] = l3;
    dst[offset + 3 * stride + 1] = l3;
    dst[offset + 3 * stride + 2] = l3;
    dst[offset + 3 * stride + 3] = l3;
  }

  #endregion

  #region helpers

  private static byte _Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
  private static byte _Avg2(int a, int b) => (byte)((a + b + 1) >> 1);
  private static byte _Avg3(int a, int b, int c) => (byte)((a + 2 * b + c + 2) >> 2);

  private static void _Fill4(byte[] dst, int offset, byte val) {
    dst[offset + 0] = val;
    dst[offset + 1] = val;
    dst[offset + 2] = val;
    dst[offset + 3] = val;
  }

  #endregion
}
