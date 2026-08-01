using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FileFormat.Jpeg;

/// <summary>YCbCr/RGB color conversion and chroma subsampling/upsampling.</summary>
internal static class JpegColorConverter {

  /// <summary>Converts an RGB image to component planes (Y,Cb,Cr or just Y for grayscale).</summary>
  public static byte[][] RgbToYCbCr(byte[] rgb, int width, int height) {
    var y = new byte[width * height];
    var cb = new byte[width * height];
    var cr = new byte[width * height];

    for (var i = 0; i < width * height; ++i) {
      var r = rgb[i * 3];
      var g = rgb[i * 3 + 1];
      var b = rgb[i * 3 + 2];

      // ITU-R BT.601: Y = 0.299R + 0.587G + 0.114B
      y[i] = (byte)Math.Clamp((19595 * r + 38470 * g + 7471 * b + 32768) >> 16, 0, 255);
      cb[i] = (byte)Math.Clamp(128 + ((-11056 * r - 21712 * g + 32768 * b + 32768) >> 16), 0, 255);
      cr[i] = (byte)Math.Clamp(128 + ((32768 * r - 27440 * g - 5328 * b + 32768) >> 16), 0, 255);
    }

    return [y, cb, cr];
  }

  /// <summary>Extracts grayscale plane from RGB data (all channels should be equal for true grayscale).</summary>
  public static byte[] RgbToGrayscale(byte[] rgb, int width, int height) {
    var y = new byte[width * height];
    for (var i = 0; i < width * height; ++i)
      y[i] = rgb[i * 3];
    return y;
  }

  /// <summary>Converts grayscale Y plane to packed RGB24 (R=G=B=Y).</summary>
  public static byte[] GrayscaleToRgb(byte[] y, int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      rgb[i * 3] = y[i];
      rgb[i * 3 + 1] = y[i];
      rgb[i * 3 + 2] = y[i];
    }

    return rgb;
  }

  /// <summary>Converts YCbCr component planes back to packed RGB24.</summary>
  /// <remarks>
  /// This was eight pixels at a time through SSE2, and the vectorised form was wrong. The fixed-point
  /// coefficients are up to 454, and the products were taken in sixteen-bit lanes: a chroma sample
  /// 126 away from neutral gives 57204, which does not fit in a signed short and wraps to a negative
  /// number. The result then clamped to the opposite end of the range.
  /// <para/>
  /// That is invisible on anything muted — the middle of a picture came out exactly right — and
  /// turns saturated colour inside out. A fully blue pixel decoded as black. Since the products
  /// genuinely need more than sixteen bits, the vector form cannot carry them without widening to
  /// thirty-two-bit lanes, at which point it is doing two pixels at a time and the entropy decoding
  /// this sits behind dominates anyway. So it does the arithmetic once, in a width that holds it.
  /// </remarks>
  public static byte[] YCbCrToRgb(byte[] yPlane, byte[] cbPlane, byte[] crPlane, int width, int height) {
    var pixelCount = width * height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var yVal = yPlane[i];
      var cbVal = cbPlane[i] - 128;
      var crVal = crPlane[i] - 128;

      var r = yVal + ((91881 * crVal + 32768) >> 16);
      var g = yVal - ((22554 * cbVal + 46802 * crVal + 32768) >> 16);
      var b = yVal + ((116130 * cbVal + 32768) >> 16);

      rgb[i * 3] = (byte)Math.Clamp(r, 0, 255);
      rgb[i * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
      rgb[i * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
    }

    return rgb;
  }

  /// <summary>Downsamples a component plane by the given factors (box filter average).</summary>
  public static byte[] Downsample(byte[] plane, int width, int height, int hFactor, int vFactor) {
    if (hFactor == 1 && vFactor == 1)
      return (byte[])plane.Clone();

    var outWidth = (width + hFactor - 1) / hFactor;
    var outHeight = (height + vFactor - 1) / vFactor;
    var result = new byte[outWidth * outHeight];

    for (var oy = 0; oy < outHeight; ++oy)
      for (var ox = 0; ox < outWidth; ++ox) {
        var sum = 0;
        var count = 0;
        for (var dy = 0; dy < vFactor; ++dy)
          for (var dx = 0; dx < hFactor; ++dx) {
            var sx = ox * hFactor + dx;
            var sy = oy * vFactor + dy;
            if (sx < width && sy < height) {
              sum += plane[sy * width + sx];
              ++count;
            }
          }

        result[oy * outWidth + ox] = (byte)(count > 0 ? (sum + count / 2) / count : 0);
      }

    return result;
  }

  /// <summary>Upsamples a component plane by the given factors (nearest-neighbor).</summary>
  /// <summary>Brings a subsampled component plane back up to the picture's size.</summary>
  /// <remarks>
  /// Chrominance is usually stored at half resolution, and how it is brought back matters more than
  /// it sounds. Repeating each sample across the block it covers — which is what this used to do —
  /// leaves a visible step at every block boundary and puts colour up to a couple of dozen levels
  /// away from where every other decoder puts it.
  /// <para/>
  /// What they do instead is a triangle filter: each output takes three parts of the sample it sits
  /// on and one part of the neighbour it sits nearer. The weights below are the integer form of
  /// that, edges included, so the result matches rather than merely being smoother.
  /// </remarks>
  public static byte[] Upsample(byte[] plane, int inWidth, int inHeight, int outWidth, int outHeight) {
    if (inWidth == outWidth && inHeight == outHeight)
      return (byte[])plane.Clone();

    var result = new byte[outWidth * outHeight];

    // A picture whose size is not a whole number of blocks leaves the chroma plane a little larger
    // than half: eight by four carries a thirteen by seven picture. So the test is that doubling
    // covers the output rather than matching it exactly, and the extra columns are simply not
    // written — which is what cropping the doubled plane amounts to.
    if (_Doubles(inWidth, outWidth) && outHeight == inHeight) {
      for (var y = 0; y < inHeight; ++y)
        _TriangleAcross(plane, y * inWidth, inWidth, result, y * outWidth, outWidth, 1);

      return result;
    }

    if (_Doubles(inWidth, outWidth) && _Doubles(inHeight, outHeight)) {
      // Vertically the same filter applies, so each output row is three parts of the row it sits on
      // and one of the row it sits nearer. Both are folded into one pass over the columns.
      var column = new int[inWidth];

      for (var oy = 0; oy < outHeight; ++oy) {
        var near = Math.Min(oy >> 1, inHeight - 1);
        var far = Math.Clamp((oy & 1) == 0 ? near - 1 : near + 1, 0, inHeight - 1);

        for (var x = 0; x < inWidth; ++x)
          column[x] = 3 * plane[near * inWidth + x] + plane[far * inWidth + x];

        _TriangleAcross(column, 0, inWidth, result, oy * outWidth, outWidth, 4);
      }

      return result;
    }

    for (var oy = 0; oy < outHeight; ++oy) {
      var sy = Math.Min(oy * inHeight / outHeight, inHeight - 1);
      for (var ox = 0; ox < outWidth; ++ox) {
        var sx = Math.Min(ox * inWidth / outWidth, inWidth - 1);
        result[oy * outWidth + ox] = plane[sy * inWidth + sx];
      }
    }

    return result;
  }

  /// <summary>Whether doubling the input covers the output without overshooting a whole sample.</summary>
  private static bool _Doubles(int input, int output) => output > input && output <= input * 2;

  /// <summary>Doubles one row with the triangle filter, replicating past either end.</summary>
  /// <param name="scale">
  /// What the incoming values are already multiplied by, so the shift can take both passes out at
  /// once when the vertical filter has already been applied.
  /// </param>
  private static void _TriangleAcross(
    ReadOnlySpan<byte> source, int from, int count, byte[] target, int to, int width, int scale)
    => _TriangleAcross(_Widen(source, from, count), 0, count, target, to, width, scale);

  private static int[] _Widen(ReadOnlySpan<byte> source, int from, int count) {
    var widened = new int[count];
    for (var i = 0; i < count; ++i)
      widened[i] = source[from + i];

    return widened;
  }

  private static void _TriangleAcross(
    ReadOnlySpan<int> source, int from, int count, byte[] target, int to, int width, int scale) {
    var shift = scale == 1 ? 2 : 4;
    var evenBias = scale == 1 ? 2 : 8;
    var oddBias = scale == 1 ? 1 : 7;

    for (var x = 0; x < count; ++x) {
      var here = source[from + x] * 3;
      var left = source[from + Math.Max(x - 1, 0)];
      var right = source[from + Math.Min(x + 1, count - 1)];

      var even = to + x * 2;
      if (even < to + width)
        target[even] = (byte)((here + left + evenBias) >> shift);
      if (even + 1 < to + width)
        target[even + 1] = (byte)((here + right + oddBias) >> shift);
    }
  }


  /// <summary>Gets the chroma H/V sampling factors for a given subsampling mode.</summary>
  public static (int hFactor, int vFactor) GetChromaFactors(JpegSubsampling subsampling) => subsampling switch {
    JpegSubsampling.Chroma444 => (1, 1),
    JpegSubsampling.Chroma422 => (2, 1),
    JpegSubsampling.Chroma420 => (2, 2),
    _ => (1, 1)
  };

  /// <summary>
  /// The transform an Adobe four-component file states in its APP14 segment.
  /// </summary>
  /// <remarks>
  /// Zero means the four planes are ink amounts already; two means they were carried as luma and
  /// two chroma differences with the key alongside, exactly as a colour picture is, and have to be
  /// brought back before they mean anything.
  /// </remarks>
  public const int AdobeTransformNone = 0;

  public const int AdobeTransformYcck = 2;

  /// <summary>Converts a four-component Adobe picture to RGB.</summary>
  /// <remarks>
  /// Adobe stores ink amounts inverted — a stored 255 means no ink — and the two transforms differ
  /// in how far that survives. Undoing the luma-chroma step of a YCCK file yields the ink amounts
  /// the right way up again, while its key plane stays inverted; an untransformed file has all four
  /// planes inverted. Treating both alike leaves one of them a negative of itself.
  /// <para/>
  /// Reading the first three planes as a colour picture and dropping the fourth, which is what this
  /// used to do, loses the black plate entirely and leaves every dark area pale.
  /// </remarks>
  public static byte[] YcckOrCmykToRgb(
    byte[] c, byte[] m, byte[] y, byte[] k, int width, int height, int transform) {
    var count = width * height;
    var rgb = new byte[count * 3];

    for (var i = 0; i < count; ++i) {
      int red, green, blue;

      if (transform == AdobeTransformYcck) {
        // The first three planes are luma and chroma; undoing that gives the ink amounts the right
        // way up, so each has to be turned back into the light it leaves.
        var luma = c[i];
        var cb = m[i] - 128;
        var cr = y[i] - 128;
        red = 255 - _Clamp(luma + ((91881 * cr) >> 16));
        green = 255 - _Clamp(luma - ((22554 * cb + 46802 * cr) >> 16));
        blue = 255 - _Clamp(luma + ((116130 * cb) >> 16));
      } else {
        // Already inverted, so the stored value is the light rather than the ink.
        red = c[i];
        green = m[i];
        blue = y[i];
      }

      // The key plane is inverted in both forms, so it scales directly.
      var key = k[i];
      rgb[i * 3] = (byte)(red * key / 255);
      rgb[i * 3 + 1] = (byte)(green * key / 255);
      rgb[i * 3 + 2] = (byte)(blue * key / 255);
    }

    return rgb;
  }

  private static int _Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
