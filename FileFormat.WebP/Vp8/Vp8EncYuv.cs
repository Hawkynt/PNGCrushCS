using System.Runtime.CompilerServices;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// RGB → YCbCr 4:2:0 conversion for the VP8 encoder.
/// BT.601 studio range (Y∈[16,235], UV∈[16,240]), matching libwebp <c>src/dsp/yuv.h</c>.
/// Chroma is computed from the 2×2 sum of source RGB pixels (sum of 4 weights = -38876/65536 ≈ -0.593 for U and so on).
/// </summary>
internal static class Vp8EncYuv {

  private const int YuvFix = 16;
  private const int YuvHalf = 1 << YuvFix - 1;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _RgbToY(int r, int g, int b) {
    var luma = 16839 * r + 33059 * g + 6420 * b;
    return luma + YuvHalf + (16 << YuvFix) >> YuvFix;
  }

  /// <summary>Clip sum-of-4-pixels UV into [0, 255]. Shift by YUV_FIX+2 = 18 performs
  /// 2×2 averaging and the fixed-point descale simultaneously.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _ClipUV(int uvSum) {
    var uv = uvSum + (YuvHalf << 2) + (128 << YuvFix + 2) >> YuvFix + 2;
    return (uv & ~0xff) == 0 ? (byte)uv : uv < 0 ? (byte)0 : (byte)255;
  }

  /// <summary>Convert RGB24 pixels → VP8 YUV420 planes.
  /// <para>Output plane sizes: Y = W·H, U/V = ⌈W/2⌉·⌈H/2⌉.
  /// For odd W or H, boundary pixels are replicated to form the 2×2 chroma averaging block.</para></summary>
  public static void Rgb24ToYuv420(byte[] rgb, int width, int height,
                                   byte[] yPlane, int yStride,
                                   byte[] uPlane, byte[] vPlane, int uvStride) {
    for (var j = 0; j < height; j += 2) {
      for (var i = 0; i < width; i += 2) {
        // Gather up to 4 source pixels in a 2×2 block, replicating edges when W/H is odd.
        var i2 = i + 1 < width ? i + 1 : i;
        var j2 = j + 1 < height ? j + 1 : j;
        var off00 = (j * width + i) * 3;
        var off01 = (j * width + i2) * 3;
        var off10 = (j2 * width + i) * 3;
        var off11 = (j2 * width + i2) * 3;

        int r00 = rgb[off00], g00 = rgb[off00 + 1], b00 = rgb[off00 + 2];
        int r01 = rgb[off01], g01 = rgb[off01 + 1], b01 = rgb[off01 + 2];
        int r10 = rgb[off10], g10 = rgb[off10 + 1], b10 = rgb[off10 + 2];
        int r11 = rgb[off11], g11 = rgb[off11 + 1], b11 = rgb[off11 + 2];

        // Per-pixel Y (written only to in-bounds positions).
        yPlane[j * yStride + i] = (byte)_RgbToY(r00, g00, b00);
        if (i + 1 < width) yPlane[j * yStride + i + 1] = (byte)_RgbToY(r01, g01, b01);
        if (j + 1 < height) yPlane[(j + 1) * yStride + i] = (byte)_RgbToY(r10, g10, b10);
        if (i + 1 < width && j + 1 < height) yPlane[(j + 1) * yStride + i + 1] = (byte)_RgbToY(r11, g11, b11);

        var sumR = r00 + r01 + r10 + r11;
        var sumG = g00 + g01 + g10 + g11;
        var sumB = b00 + b01 + b10 + b11;

        var uSum = -9719 * sumR - 19081 * sumG + 28800 * sumB;
        var vSum = 28800 * sumR - 24116 * sumG - 4684 * sumB;

        uPlane[(j >> 1) * uvStride + (i >> 1)] = _ClipUV(uSum);
        vPlane[(j >> 1) * uvStride + (i >> 1)] = _ClipUV(vSum);
      }
    }
  }

  /// <summary>Convert RGBA32 (ignoring alpha) → VP8 YUV420 planes.</summary>
  public static void Rgba32ToYuv420(byte[] rgba, int width, int height,
                                    byte[] yPlane, int yStride,
                                    byte[] uPlane, byte[] vPlane, int uvStride) {
    for (var j = 0; j < height; j += 2) {
      for (var i = 0; i < width; i += 2) {
        var i2 = i + 1 < width ? i + 1 : i;
        var j2 = j + 1 < height ? j + 1 : j;
        var off00 = (j * width + i) * 4;
        var off01 = (j * width + i2) * 4;
        var off10 = (j2 * width + i) * 4;
        var off11 = (j2 * width + i2) * 4;

        int r00 = rgba[off00], g00 = rgba[off00 + 1], b00 = rgba[off00 + 2];
        int r01 = rgba[off01], g01 = rgba[off01 + 1], b01 = rgba[off01 + 2];
        int r10 = rgba[off10], g10 = rgba[off10 + 1], b10 = rgba[off10 + 2];
        int r11 = rgba[off11], g11 = rgba[off11 + 1], b11 = rgba[off11 + 2];

        yPlane[j * yStride + i] = (byte)_RgbToY(r00, g00, b00);
        if (i + 1 < width) yPlane[j * yStride + i + 1] = (byte)_RgbToY(r01, g01, b01);
        if (j + 1 < height) yPlane[(j + 1) * yStride + i] = (byte)_RgbToY(r10, g10, b10);
        if (i + 1 < width && j + 1 < height) yPlane[(j + 1) * yStride + i + 1] = (byte)_RgbToY(r11, g11, b11);

        var sumR = r00 + r01 + r10 + r11;
        var sumG = g00 + g01 + g10 + g11;
        var sumB = b00 + b01 + b10 + b11;

        uPlane[(j >> 1) * uvStride + (i >> 1)] = _ClipUV(-9719 * sumR - 19081 * sumG + 28800 * sumB);
        vPlane[(j >> 1) * uvStride + (i >> 1)] = _ClipUV(28800 * sumR - 24116 * sumG - 4684 * sumB);
      }
    }
  }
}
