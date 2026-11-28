using System;

namespace FileFormat.Codecs.Svq3;

/// <summary>
/// The H.264 intra predictors SVQ3 uses, with SVQ3's own diagonal-down-left 4x4 and 16x16 plane
/// predictors: FFmpeg's <c>libavcodec/h264pred.c</c> and <c>h264pred_template.c</c> (LGPL-2.1-or-later)
/// as <c>ff_h264_pred_init</c> sets them up for <c>AV_CODEC_ID_SVQ3</c> at eight bits.
/// </summary>
/// <remarks>
/// Every predictor writes into a plane at an offset and reads the samples above, to the left and
/// above-left of it there; which of those exist is the caller's business, settled the way H.264
/// settles it by swapping a mode for its DC variant.
/// </remarks>
internal static class Svq3IntraPrediction {

  internal const int VerticalPrediction = 0;
  internal const int HorizontalPrediction = 1;
  internal const int DcPrediction = 2;
  internal const int DiagonalDownLeftPrediction = 3;
  internal const int DiagonalDownRightPrediction = 4;
  internal const int VerticalRightPrediction = 5;
  internal const int HorizontalDownPrediction = 6;
  internal const int VerticalLeftPrediction = 7;
  internal const int HorizontalUpPrediction = 8;
  internal const int LeftDcPrediction = 9;
  internal const int TopDcPrediction = 10;
  internal const int Dc128Prediction = 11;

  internal const int Dc8x8 = 0;
  internal const int Horizontal8x8 = 1;
  internal const int Vertical8x8 = 2;
  internal const int Plane8x8 = 3;
  internal const int LeftDc8x8 = 4;
  internal const int TopDc8x8 = 5;
  internal const int Dc128x8 = 6;

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  /// <summary><c>pred4x4[mode]</c>; the top-right samples are read at <paramref name="topRight"/>.</summary>
  internal static void Predict4x4(int mode, byte[] p, int s, int stride, int topRight) {
    int L(int i) => p[s - 1 + i * stride];
    int T(int i) => p[s - stride + i];
    int Tr(int i) => p[topRight + i];
    void Set(int x, int y, int v) => p[s + x + y * stride] = (byte)v;
    void Fill(int v) {
      for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 4; ++x)
          Set(x, y, v);
    }

    switch (mode) {
      case VerticalPrediction:
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            Set(x, y, T(x));
        return;
      case HorizontalPrediction:
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            Set(x, y, L(y));
        return;
      case DcPrediction:
        Fill((T(0) + T(1) + T(2) + T(3) + L(0) + L(1) + L(2) + L(3) + 4) >> 3);
        return;
      case LeftDcPrediction:
        Fill((L(0) + L(1) + L(2) + L(3) + 2) >> 2);
        return;
      case TopDcPrediction:
        Fill((T(0) + T(1) + T(2) + T(3) + 2) >> 2);
        return;
      case Dc128Prediction:
        Fill(128);
        return;
      case DiagonalDownLeftPrediction: {
        // SVQ3's own: the means of the left column and the top row, element by element.
        Set(0, 0, (L(1) + T(1)) >> 1);
        Set(1, 0, (L(2) + T(2)) >> 1);
        Set(0, 1, (L(2) + T(2)) >> 1);
        var v = (L(3) + T(3)) >> 1;
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            if (x + y >= 2)
              Set(x, y, v);
        return;
      }
      case DiagonalDownRightPrediction: {
        int lt = p[s - 1 - stride], t0 = T(0), t1 = T(1), t2 = T(2), t3 = T(3), l0 = L(0), l1 = L(1), l2 = L(2), l3 = L(3);
        Set(0, 3, (l3 + 2 * l2 + l1 + 2) >> 2);
        Set(0, 2, (l2 + 2 * l1 + l0 + 2) >> 2); Set(1, 3, (l2 + 2 * l1 + l0 + 2) >> 2);
        var a = (l1 + 2 * l0 + lt + 2) >> 2;
        Set(0, 1, a); Set(1, 2, a); Set(2, 3, a);
        a = (l0 + 2 * lt + t0 + 2) >> 2;
        Set(0, 0, a); Set(1, 1, a); Set(2, 2, a); Set(3, 3, a);
        a = (lt + 2 * t0 + t1 + 2) >> 2;
        Set(1, 0, a); Set(2, 1, a); Set(3, 2, a);
        a = (t0 + 2 * t1 + t2 + 2) >> 2;
        Set(2, 0, a); Set(3, 1, a);
        Set(3, 0, (t1 + 2 * t2 + t3 + 2) >> 2);
        return;
      }
      case VerticalRightPrediction: {
        int lt = p[s - 1 - stride], t0 = T(0), t1 = T(1), t2 = T(2), t3 = T(3), l0 = L(0), l1 = L(1), l2 = L(2);
        Set(0, 0, (lt + t0 + 1) >> 1); Set(1, 2, (lt + t0 + 1) >> 1);
        Set(1, 0, (t0 + t1 + 1) >> 1); Set(2, 2, (t0 + t1 + 1) >> 1);
        Set(2, 0, (t1 + t2 + 1) >> 1); Set(3, 2, (t1 + t2 + 1) >> 1);
        Set(3, 0, (t2 + t3 + 1) >> 1);
        Set(0, 1, (l0 + 2 * lt + t0 + 2) >> 2); Set(1, 3, (l0 + 2 * lt + t0 + 2) >> 2);
        Set(1, 1, (lt + 2 * t0 + t1 + 2) >> 2); Set(2, 3, (lt + 2 * t0 + t1 + 2) >> 2);
        Set(2, 1, (t0 + 2 * t1 + t2 + 2) >> 2); Set(3, 3, (t0 + 2 * t1 + t2 + 2) >> 2);
        Set(3, 1, (t1 + 2 * t2 + t3 + 2) >> 2);
        Set(0, 2, (lt + 2 * l0 + l1 + 2) >> 2);
        Set(0, 3, (l0 + 2 * l1 + l2 + 2) >> 2);
        return;
      }
      case HorizontalDownPrediction: {
        int lt = p[s - 1 - stride], t0 = T(0), t1 = T(1), t2 = T(2), l0 = L(0), l1 = L(1), l2 = L(2), l3 = L(3);
        Set(0, 0, (lt + l0 + 1) >> 1); Set(2, 1, (lt + l0 + 1) >> 1);
        Set(1, 0, (l0 + 2 * lt + t0 + 2) >> 2); Set(3, 1, (l0 + 2 * lt + t0 + 2) >> 2);
        Set(2, 0, (lt + 2 * t0 + t1 + 2) >> 2);
        Set(3, 0, (t0 + 2 * t1 + t2 + 2) >> 2);
        Set(0, 1, (l0 + l1 + 1) >> 1); Set(2, 2, (l0 + l1 + 1) >> 1);
        Set(1, 1, (lt + 2 * l0 + l1 + 2) >> 2); Set(3, 2, (lt + 2 * l0 + l1 + 2) >> 2);
        Set(0, 2, (l1 + l2 + 1) >> 1); Set(2, 3, (l1 + l2 + 1) >> 1);
        Set(1, 2, (l0 + 2 * l1 + l2 + 2) >> 2); Set(3, 3, (l0 + 2 * l1 + l2 + 2) >> 2);
        Set(0, 3, (l2 + l3 + 1) >> 1);
        Set(1, 3, (l1 + 2 * l2 + l3 + 2) >> 2);
        return;
      }
      case VerticalLeftPrediction: {
        int t0 = T(0), t1 = T(1), t2 = T(2), t3 = T(3), t4 = Tr(0), t5 = Tr(1), t6 = Tr(2);
        Set(0, 0, (t0 + t1 + 1) >> 1);
        Set(1, 0, (t1 + t2 + 1) >> 1); Set(0, 2, (t1 + t2 + 1) >> 1);
        Set(2, 0, (t2 + t3 + 1) >> 1); Set(1, 2, (t2 + t3 + 1) >> 1);
        Set(3, 0, (t3 + t4 + 1) >> 1); Set(2, 2, (t3 + t4 + 1) >> 1);
        Set(3, 2, (t4 + t5 + 1) >> 1);
        Set(0, 1, (t0 + 2 * t1 + t2 + 2) >> 2);
        Set(1, 1, (t1 + 2 * t2 + t3 + 2) >> 2); Set(0, 3, (t1 + 2 * t2 + t3 + 2) >> 2);
        Set(2, 1, (t2 + 2 * t3 + t4 + 2) >> 2); Set(1, 3, (t2 + 2 * t3 + t4 + 2) >> 2);
        Set(3, 1, (t3 + 2 * t4 + t5 + 2) >> 2); Set(2, 3, (t3 + 2 * t4 + t5 + 2) >> 2);
        Set(3, 3, (t4 + 2 * t5 + t6 + 2) >> 2);
        return;
      }
      default: {
        int l0 = L(0), l1 = L(1), l2 = L(2), l3 = L(3);
        Set(0, 0, (l0 + l1 + 1) >> 1);
        Set(1, 0, (l0 + 2 * l1 + l2 + 2) >> 2);
        Set(2, 0, (l1 + l2 + 1) >> 1); Set(0, 1, (l1 + l2 + 1) >> 1);
        Set(3, 0, (l1 + 2 * l2 + l3 + 2) >> 2); Set(1, 1, (l1 + 2 * l2 + l3 + 2) >> 2);
        Set(2, 1, (l2 + l3 + 1) >> 1); Set(0, 2, (l2 + l3 + 1) >> 1);
        Set(3, 1, (l2 + 2 * l3 + l3 + 2) >> 2); Set(1, 2, (l2 + 2 * l3 + l3 + 2) >> 2);
        Set(3, 2, l3); Set(1, 3, l3); Set(0, 3, l3); Set(2, 2, l3); Set(2, 3, l3); Set(3, 3, l3);
        return;
      }
    }
  }

  /// <summary><c>pred16x16[mode]</c>, with SVQ3's plane predictor.</summary>
  internal static void Predict16x16(int mode, byte[] p, int s, int stride) {
    void Fill(int v) {
      for (var y = 0; y < 16; ++y)
        p.AsSpan(s + y * stride, 16).Fill((byte)v);
    }

    switch (mode) {
      case Dc8x8: {
        var dc = 0;
        for (var i = 0; i < 16; ++i)
          dc += p[s - 1 + i * stride] + p[s + i - stride];
        Fill((dc + 16) >> 5);
        return;
      }
      case Horizontal8x8:
        for (var y = 0; y < 16; ++y)
          p.AsSpan(s + y * stride, 16).Fill(p[s - 1 + y * stride]);
        return;
      case Vertical8x8:
        for (var y = 0; y < 16; ++y)
          Array.Copy(p, s - stride, p, s + y * stride, 16);
        return;
      case Plane8x8: {
        var src0 = s + 7 - stride;
        var src1 = s + 8 * stride - 1;
        var src2 = src1 - 2 * stride;
        var h = p[src0 + 1] - p[src0 - 1];
        var v = p[src1] - p[src2];
        for (var k = 2; k <= 8; ++k) {
          src1 += stride;
          src2 -= stride;
          h += k * (p[src0 + k] - p[src0 - k]);
          v += k * (p[src1] - p[src2]);
        }

        h = 5 * (h / 4) / 16;
        v = 5 * (v / 4) / 16;
        (h, v) = (v, h);

        var a = 16 * (p[src1] + p[src2 + 16] + 1) - 7 * (v + h);
        for (var j = 0; j < 16; ++j) {
          var b = a;
          a += v;
          for (var i = 0; i < 16; ++i, b += h)
            p[s + j * stride + i] = _Clip(b >> 5);
        }

        return;
      }
      case LeftDc8x8: {
        var dc = 0;
        for (var i = 0; i < 16; ++i)
          dc += p[s - 1 + i * stride];
        Fill((dc + 8) >> 4);
        return;
      }
      case TopDc8x8: {
        var dc = 0;
        for (var i = 0; i < 16; ++i)
          dc += p[s + i - stride];
        Fill((dc + 8) >> 4);
        return;
      }
      default:
        Fill(128);
        return;
    }
  }

  /// <summary><c>pred8x8[mode]</c> for an 8x8 chrominance block.</summary>
  internal static void Predict8x8(int mode, byte[] p, int s, int stride) {
    void Quadrant(int x0, int y0, int v) {
      for (var y = 0; y < 4; ++y)
        p.AsSpan(s + (y0 + y) * stride + x0, 4).Fill((byte)v);
    }

    switch (mode) {
      case Dc8x8: {
        int dc0 = 0, dc1 = 0, dc2 = 0;
        for (var i = 0; i < 4; ++i) {
          dc0 += p[s - 1 + i * stride] + p[s + i - stride];
          dc1 += p[s + 4 + i - stride];
          dc2 += p[s - 1 + (i + 4) * stride];
        }

        Quadrant(0, 0, (dc0 + 4) >> 3);
        Quadrant(4, 0, (dc1 + 2) >> 2);
        Quadrant(0, 4, (dc2 + 2) >> 2);
        Quadrant(4, 4, (dc1 + dc2 + 4) >> 3);
        return;
      }
      case Horizontal8x8:
        for (var y = 0; y < 8; ++y)
          p.AsSpan(s + y * stride, 8).Fill(p[s - 1 + y * stride]);
        return;
      case Vertical8x8:
        for (var y = 0; y < 8; ++y)
          Array.Copy(p, s - stride, p, s + y * stride, 8);
        return;
      case Plane8x8: {
        var src0 = s + 3 - stride;
        var src1 = s + 4 * stride - 1;
        var src2 = src1 - 2 * stride;
        var h = p[src0 + 1] - p[src0 - 1];
        var v = p[src1] - p[src2];
        for (var k = 2; k <= 4; ++k) {
          src1 += stride;
          src2 -= stride;
          h += k * (p[src0 + k] - p[src0 - k]);
          v += k * (p[src1] - p[src2]);
        }

        h = (17 * h + 16) >> 5;
        v = (17 * v + 16) >> 5;
        var a = 16 * (p[src1] + p[src2 + 8] + 1) - 3 * (v + h);
        for (var j = 0; j < 8; ++j) {
          var b = a;
          a += v;
          for (var i = 0; i < 8; ++i, b += h)
            p[s + j * stride + i] = _Clip(b >> 5);
        }

        return;
      }
      case LeftDc8x8: {
        int dc0 = 0, dc2 = 0;
        for (var i = 0; i < 4; ++i) {
          dc0 += p[s - 1 + i * stride];
          dc2 += p[s - 1 + (i + 4) * stride];
        }

        Quadrant(0, 0, (dc0 + 2) >> 2);
        Quadrant(4, 0, (dc0 + 2) >> 2);
        Quadrant(0, 4, (dc2 + 2) >> 2);
        Quadrant(4, 4, (dc2 + 2) >> 2);
        return;
      }
      case TopDc8x8: {
        int dc0 = 0, dc1 = 0;
        for (var i = 0; i < 4; ++i) {
          dc0 += p[s + i - stride];
          dc1 += p[s + 4 + i - stride];
        }

        Quadrant(0, 0, (dc0 + 2) >> 2);
        Quadrant(4, 0, (dc1 + 2) >> 2);
        Quadrant(0, 4, (dc0 + 2) >> 2);
        Quadrant(4, 4, (dc1 + 2) >> 2);
        return;
      }
      default:
        for (var y = 0; y < 8; ++y)
          p.AsSpan(s + y * stride, 8).Fill(128);
        return;
    }
  }
}
