using System.Runtime.CompilerServices;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// Forward / inverse DCT and WHT for the VP8 encoder (§14.3, §14.4).
/// Scalar (non-SIMD) port of libwebp <c>src/dsp/enc.c</c> reference implementations.
/// All buffers use <c>BPS = 32</c> stride (the encoder's block-workspace row stride).
/// </summary>
internal static class Vp8EncTransform {

  public const int Bps = 32;

  private const int _Ac3C1 = 20091; // cos(π/8)·√2 fractional (≈1.306)
  private const int _Ac3C2 = 35468; // sin(π/8)·√2 fractional (≈0.541)

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Mul1(int a) => (a * _Ac3C1 >> 16) + a;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Mul2(int a) => a * _Ac3C2 >> 16;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _Clip8(int v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;

  /// <summary>Forward 4x4 DCT: out[16] = DCT(src[4x4, stride=BPS] - ref[4x4, stride=BPS]).</summary>
  public static void FTransform(byte[] src, int srcOff, byte[] refBuf, int refOff, short[] outCoeffs, int outOff) {
    var tmp = new int[16];
    for (var i = 0; i < 4; ++i) {
      var d0 = src[srcOff + 0] - refBuf[refOff + 0];
      var d1 = src[srcOff + 1] - refBuf[refOff + 1];
      var d2 = src[srcOff + 2] - refBuf[refOff + 2];
      var d3 = src[srcOff + 3] - refBuf[refOff + 3];
      var a0 = d0 + d3;
      var a1 = d1 + d2;
      var a2 = d1 - d2;
      var a3 = d0 - d3;
      tmp[0 + i * 4] = (a0 + a1) * 8;
      tmp[1 + i * 4] = a2 * 2217 + a3 * 5352 + 1812 >> 9;
      tmp[2 + i * 4] = (a0 - a1) * 8;
      tmp[3 + i * 4] = a3 * 2217 - a2 * 5352 + 937 >> 9;
      srcOff += Bps;
      refOff += Bps;
    }
    for (var i = 0; i < 4; ++i) {
      var a0 = tmp[0 + i] + tmp[12 + i];
      var a1 = tmp[4 + i] + tmp[8 + i];
      var a2 = tmp[4 + i] - tmp[8 + i];
      var a3 = tmp[0 + i] - tmp[12 + i];
      outCoeffs[outOff + 0 + i] = (short)(a0 + a1 + 7 >> 4);
      outCoeffs[outOff + 4 + i] = (short)((a2 * 2217 + a3 * 5352 + 12000 >> 16) + (a3 != 0 ? 1 : 0));
      outCoeffs[outOff + 8 + i] = (short)(a0 - a1 + 7 >> 4);
      outCoeffs[outOff + 12 + i] = (short)(a3 * 2217 - a2 * 5352 + 51000 >> 16);
    }
  }

  /// <summary>FTransform for two adjacent 4x4 blocks (src, src+4) into (out, out+16).</summary>
  public static void FTransform2(byte[] src, int srcOff, byte[] refBuf, int refOff, short[] outCoeffs, int outOff) {
    FTransform(src, srcOff, refBuf, refOff, outCoeffs, outOff);
    FTransform(src, srcOff + 4, refBuf, refOff + 4, outCoeffs, outOff + 16);
  }

  /// <summary>Forward Walsh-Hadamard Transform of the 16 DC coefficients (Y2 block).
  /// Input is 16 DCs picked from 4x4 macroblocks (read at stride 64 — see caller convention).</summary>
  public static void FTransformWHT(short[] input, int inOff, short[] output, int outOff) {
    var tmp = new int[16];
    for (var i = 0; i < 4; ++i) {
      var a0 = input[inOff + 0 * 16] + input[inOff + 2 * 16];
      var a1 = input[inOff + 1 * 16] + input[inOff + 3 * 16];
      var a2 = input[inOff + 1 * 16] - input[inOff + 3 * 16];
      var a3 = input[inOff + 0 * 16] - input[inOff + 2 * 16];
      tmp[0 + i * 4] = a0 + a1;
      tmp[1 + i * 4] = a3 + a2;
      tmp[2 + i * 4] = a3 - a2;
      tmp[3 + i * 4] = a0 - a1;
      inOff += 64;
    }
    for (var i = 0; i < 4; ++i) {
      var a0 = tmp[0 + i] + tmp[8 + i];
      var a1 = tmp[4 + i] + tmp[12 + i];
      var a2 = tmp[4 + i] - tmp[12 + i];
      var a3 = tmp[0 + i] - tmp[8 + i];
      var b0 = a0 + a1;
      var b1 = a3 + a2;
      var b2 = a3 - a2;
      var b3 = a0 - a1;
      output[outOff + 0 + i] = (short)(b0 >> 1);
      output[outOff + 4 + i] = (short)(b1 >> 1);
      output[outOff + 8 + i] = (short)(b2 >> 1);
      output[outOff + 12 + i] = (short)(b3 >> 1);
    }
  }

  /// <summary>Inverse 4x4 DCT used by the encoder for reconstruction:
  /// <c>dst[i,j] = clip8(ref[i,j] + IDCT(in)[i,j])</c>. Processes one or two adjacent blocks.</summary>
  public static void ITransform(byte[] refBuf, int refOff, short[] input, int inOff, byte[] dst, int dstOff, bool doTwo) {
    _ITransformOne(refBuf, refOff, input, inOff, dst, dstOff);
    if (doTwo) _ITransformOne(refBuf, refOff + 4, input, inOff + 16, dst, dstOff + 4);
  }

  private static void _ITransformOne(byte[] refBuf, int refOff, short[] input, int inOff, byte[] dst, int dstOff) {
    var c = new int[16];
    var tmp = 0;
    for (var i = 0; i < 4; ++i) {
      var a = input[inOff + 0] + input[inOff + 8];
      var b = input[inOff + 0] - input[inOff + 8];
      var cc = _Mul2(input[inOff + 4]) - _Mul1(input[inOff + 12]);
      var d = _Mul1(input[inOff + 4]) + _Mul2(input[inOff + 12]);
      c[tmp + 0] = a + d;
      c[tmp + 1] = b + cc;
      c[tmp + 2] = b - cc;
      c[tmp + 3] = a - d;
      tmp += 4;
      ++inOff;
    }
    tmp = 0;
    for (var i = 0; i < 4; ++i) {
      var dc = c[tmp + 0] + 4;
      var a = dc + c[tmp + 8];
      var b = dc - c[tmp + 8];
      var cc = _Mul2(c[tmp + 4]) - _Mul1(c[tmp + 12]);
      var d = _Mul1(c[tmp + 4]) + _Mul2(c[tmp + 12]);
      dst[dstOff + 0 + i * Bps] = _Clip8(refBuf[refOff + 0 + i * Bps] + (a + d >> 3));
      dst[dstOff + 1 + i * Bps] = _Clip8(refBuf[refOff + 1 + i * Bps] + (b + cc >> 3));
      dst[dstOff + 2 + i * Bps] = _Clip8(refBuf[refOff + 2 + i * Bps] + (b - cc >> 3));
      dst[dstOff + 3 + i * Bps] = _Clip8(refBuf[refOff + 3 + i * Bps] + (a - d >> 3));
      ++tmp;
    }
  }
}
