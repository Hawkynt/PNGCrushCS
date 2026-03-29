using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>Implements the AV1 deblocking loop filter, CDEF (Constrained Directional Enhancement Filter),
/// and Loop Restoration (Wiener and Self-guided Sgrproj) as post-processing filters.</summary>
internal static class Av1LoopFilter {

  /// <summary>Applies deblocking loop filter to the reconstructed frame.</summary>
  public static void ApplyDeblocking(
    short[][] planes,
    int[] planeWidths,
    int[] planeHeights,
    int[] planeStrides,
    Av1FrameHeader fh,
    Av1SequenceHeader seq
  ) {
    // Apply vertical edge filtering, then horizontal edge filtering
    for (var plane = 0; plane < seq.NumPlanes; ++plane) {
      var level = plane < 2 ? fh.LoopFilterLevel[plane] : fh.LoopFilterLevel[plane];
      if (level == 0)
        continue;

      var w = planeWidths[plane];
      var h = planeHeights[plane];
      var stride = planeStrides[plane];
      var pixels = planes[plane];
      var maxVal = (1 << seq.BitDepth) - 1;

      // Vertical edges (filter along each row)
      for (var y = 0; y < h; ++y) {
        for (var x = 4; x < w; x += 4) {
          _FilterEdge4(pixels, y * stride + x, 1, level, fh.LoopFilterSharpness, maxVal);
        }
      }

      // Horizontal edges (filter along each column)
      for (var x = 0; x < w; ++x) {
        for (var y = 4; y < h; y += 4) {
          _FilterEdge4(pixels, y * stride + x, stride, level, fh.LoopFilterSharpness, maxVal);
        }
      }
    }
  }

  private static void _FilterEdge4(short[] pixels, int idx, int step, int level, int sharpness, int maxVal) {
    // 4-tap deblocking filter
    var p1 = pixels[idx - 2 * step];
    var p0 = pixels[idx - step];
    var q0 = pixels[idx];
    var q1 = pixels[idx + step];

    var hevThresh = level > 0 ? (level >> 4) + 1 : 0;
    var limit = _GetLoopFilterLimit(level, sharpness);
    var blimit = 2 * (level + 2) + limit;

    var mask = Math.Abs(p0 - q0) * 2 + (Math.Abs(p1 - q1) >> 1);
    if (mask > blimit)
      return;

    var hev = Math.Abs(p1 - p0) > hevThresh || Math.Abs(q1 - q0) > hevThresh;

    var filter = Math.Clamp(p0 - q0, -128, 127);
    if (hev) {
      filter = Math.Clamp(filter + 3 * (q0 - p0), -128, 127);
    } else {
      filter = Math.Clamp(3 * (q0 - p0), -128, 127);
    }

    var f1 = Math.Min(filter + 4, 127) >> 3;
    var f2 = Math.Min(filter + 3, 127) >> 3;

    pixels[idx - step] = (short)Math.Clamp(p0 + f2, 0, maxVal);
    pixels[idx] = (short)Math.Clamp(q0 - f1, 0, maxVal);

    if (!hev) {
      f1 = (f1 + 1) >> 1;
      pixels[idx - 2 * step] = (short)Math.Clamp(p1 + f1, 0, maxVal);
      pixels[idx + step] = (short)Math.Clamp(q1 - f1, 0, maxVal);
    }
  }

  private static int _GetLoopFilterLimit(int level, int sharpness) {
    var limit = level;
    if (sharpness > 0) {
      limit >>= (sharpness + 3) >> 2;
      limit = Math.Min(limit, 9 - sharpness);
    }
    return Math.Max(limit, 1);
  }

  /// <summary>Applies CDEF (Constrained Directional Enhancement Filter) to the frame.</summary>
  public static void ApplyCdef(
    short[][] planes,
    int[] planeWidths,
    int[] planeHeights,
    int[] planeStrides,
    Av1FrameHeader fh,
    Av1SequenceHeader seq
  ) {
    if (!seq.EnableCdef || fh.CdefBits == 0)
      return;

    var bitDepth = seq.BitDepth;
    var maxVal = (1 << bitDepth) - 1;
    var damping = fh.CdefDamping;

    // For simplicity in still images, use strength index 0 for the whole frame
    var yPri = fh.CdefYPriStrength.Length > 0 ? fh.CdefYPriStrength[0] : 0;
    var ySec = fh.CdefYSecStrength.Length > 0 ? fh.CdefYSecStrength[0] : 0;
    var uvPri = fh.CdefUvPriStrength.Length > 0 ? fh.CdefUvPriStrength[0] : 0;
    var uvSec = fh.CdefUvSecStrength.Length > 0 ? fh.CdefUvSecStrength[0] : 0;

    for (var plane = 0; plane < seq.NumPlanes; ++plane) {
      var priStrength = plane == 0 ? yPri : uvPri;
      var secStrength = plane == 0 ? ySec : uvSec;
      if (priStrength == 0 && secStrength == 0)
        continue;

      var w = planeWidths[plane];
      var h = planeHeights[plane];
      var stride = planeStrides[plane];
      var src = planes[plane];
      var dst = new short[src.Length];
      Array.Copy(src, dst, src.Length);

      // Process 8x8 blocks (or 4x4 for chroma with subsampling)
      var blockSize = plane == 0 ? 8 : (seq.SubsamplingX != 0 ? 4 : 8);
      for (var by = 0; by < h; by += blockSize) {
        for (var bx = 0; bx < w; bx += blockSize) {
          _CdefFilterBlock(src, dst, stride, bx, by,
            Math.Min(blockSize, w - bx), Math.Min(blockSize, h - by),
            priStrength, secStrength, damping, maxVal, w, h);
        }
      }

      Array.Copy(dst, planes[plane], dst.Length);
    }
  }

  private static void _CdefFilterBlock(
    short[] src, short[] dst, int stride,
    int bx, int by, int bw, int bh,
    int priStrength, int secStrength, int damping,
    int maxVal, int planeW, int planeH
  ) {
    // Find dominant direction using primary strength
    var bestDir = 0;
    if (priStrength > 0) {
      var bestVariance = -1;
      for (var d = 0; d < 8; ++d) {
        var variance = _CdefDirection(src, stride, bx, by, bw, bh, d, planeW, planeH);
        if (variance > bestVariance) {
          bestVariance = variance;
          bestDir = d;
        }
      }
    }

    // Apply filtering
    var priDampShift = Math.Max(0, damping - _FloorLog2(priStrength));
    var secDampShift = Math.Max(0, damping - _FloorLog2(secStrength));

    for (var y = by; y < by + bh && y < planeH; ++y) {
      for (var x = bx; x < bx + bw && x < planeW; ++x) {
        var sum = 0;
        var center = src[y * stride + x];

        // Primary taps (along best direction)
        if (priStrength > 0) {
          for (var t = 0; t < 2; ++t) {
            var (dx, dy) = _CDEF_DIRECTIONS[bestDir][t];
            var sx = x + dx;
            var sy = y + dy;
            if (sx >= 0 && sx < planeW && sy >= 0 && sy < planeH) {
              var diff = src[sy * stride + sx] - center;
              sum += _CdefConstrain(diff, priStrength, priDampShift) * (2 - t);
            }
            sx = x - dx;
            sy = y - dy;
            if (sx >= 0 && sx < planeW && sy >= 0 && sy < planeH) {
              var diff = src[sy * stride + sx] - center;
              sum += _CdefConstrain(diff, priStrength, priDampShift) * (2 - t);
            }
          }
        }

        // Secondary taps (perpendicular to best direction)
        if (secStrength > 0) {
          var secDir1 = (bestDir + 2) & 7;
          var secDir2 = (bestDir + 6) & 7;
          foreach (var secDir in new[] { secDir1, secDir2 }) {
            var (dx, dy) = _CDEF_DIRECTIONS[secDir][0];
            var sx = x + dx;
            var sy = y + dy;
            if (sx >= 0 && sx < planeW && sy >= 0 && sy < planeH) {
              var diff = src[sy * stride + sx] - center;
              sum += _CdefConstrain(diff, secStrength, secDampShift);
            }
            sx = x - dx;
            sy = y - dy;
            if (sx >= 0 && sx < planeW && sy >= 0 && sy < planeH) {
              var diff = src[sy * stride + sx] - center;
              sum += _CdefConstrain(diff, secStrength, secDampShift);
            }
          }
        }

        dst[y * stride + x] = (short)Math.Clamp(center + ((sum + 8) >> 4), 0, maxVal);
      }
    }
  }

  // CDEF direction vectors: 8 directions x 2 taps (primary, secondary offset)
  private static readonly (int dx, int dy)[][] _CDEF_DIRECTIONS = [
    [(1, 0), (2, 0)],     // 0: horizontal
    [(1, 0), (2, 1)],     // 1: 22.5 degrees
    [(1, 1), (2, 2)],     // 2: 45 degrees
    [(0, 1), (1, 2)],     // 3: 67.5 degrees
    [(0, 1), (0, 2)],     // 4: vertical
    [(-1, 1), (-2, 2)],   // 5: 112.5 degrees (inverse of dir 3)
    [(-1, 1), (-2, 2)],   // 6: 135 degrees
    [(-1, 0), (-2, 1)],   // 7: 157.5 degrees
  ];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _CdefConstrain(int diff, int strength, int dampShift) {
    if (strength == 0)
      return 0;
    var absDiff = Math.Abs(diff);
    var clamped = Math.Min(absDiff, Math.Max(0, strength - (absDiff >> dampShift)));
    return diff < 0 ? -clamped : clamped;
  }

  private static int _CdefDirection(short[] src, int stride, int bx, int by, int bw, int bh, int dir, int planeW, int planeH) {
    var (dx, dy) = _CDEF_DIRECTIONS[dir][0];
    var variance = 0;
    for (var y = by; y < by + bh && y < planeH; ++y) {
      for (var x = bx; x < bx + bw && x < planeW; ++x) {
        var sx = x + dx;
        var sy = y + dy;
        if (sx >= 0 && sx < planeW && sy >= 0 && sy < planeH) {
          var diff = src[sy * stride + sx] - src[y * stride + x];
          variance += diff * diff;
        }
      }
    }
    return variance;
  }

  /// <summary>Applies Loop Restoration filters (Wiener or Self-guided) to the frame.</summary>
  public static void ApplyLoopRestoration(
    short[][] planes,
    int[] planeWidths,
    int[] planeHeights,
    int[] planeStrides,
    Av1FrameHeader fh,
    Av1SequenceHeader seq
  ) {
    if (!seq.EnableRestoration)
      return;

    for (var plane = 0; plane < seq.NumPlanes; ++plane) {
      var lrType = fh.LrType[plane];
      if (lrType == 0)
        continue;

      var w = planeWidths[plane];
      var h = planeHeights[plane];
      var stride = planeStrides[plane];
      var pixels = planes[plane];
      var maxVal = (1 << seq.BitDepth) - 1;

      switch (lrType) {
        case 1: // Switchable (pick best per unit - decoded from bitstream, default to Wiener)
        case 2: // Wiener
          _ApplyWienerFilter(pixels, stride, w, h, maxVal);
          break;
        case 3: // Self-guided projection (Sgrproj)
          _ApplySgrprojFilter(pixels, stride, w, h, maxVal, seq.BitDepth);
          break;
      }
    }
  }

  private static void _ApplyWienerFilter(short[] pixels, int stride, int w, int h, int maxVal) {
    // Default Wiener 7-tap symmetric filter coefficients
    // Using a simple 3-tap filter approximation for the default case
    var temp = new short[pixels.Length];
    Array.Copy(pixels, temp, pixels.Length);

    // Horizontal pass
    for (var y = 0; y < h; ++y) {
      for (var x = 1; x < w - 1; ++x) {
        var idx = y * stride + x;
        var sum = temp[idx - 1] + temp[idx] * 2 + temp[idx + 1];
        pixels[idx] = (short)Math.Clamp((sum + 2) >> 2, 0, maxVal);
      }
    }

    // Vertical pass
    Array.Copy(pixels, temp, pixels.Length);
    for (var y = 1; y < h - 1; ++y) {
      for (var x = 0; x < w; ++x) {
        var idx = y * stride + x;
        var sum = temp[idx - stride] + temp[idx] * 2 + temp[idx + stride];
        pixels[idx] = (short)Math.Clamp((sum + 2) >> 2, 0, maxVal);
      }
    }
  }

  private static void _ApplySgrprojFilter(short[] pixels, int stride, int w, int h, int maxVal, int bitDepth) {
    // Self-guided filter: compute integral image, apply box filter, project
    if (w < 3 || h < 3)
      return;

    var temp = new short[pixels.Length];
    Array.Copy(pixels, temp, pixels.Length);

    // Simple 3x3 box filter as approximation of self-guided restoration
    for (var y = 1; y < h - 1; ++y) {
      for (var x = 1; x < w - 1; ++x) {
        var idx = y * stride + x;
        var sum = 0;
        for (var dy = -1; dy <= 1; ++dy)
          for (var dx = -1; dx <= 1; ++dx)
            sum += temp[(y + dy) * stride + (x + dx)];

        // Weighted average between original and filtered
        var filtered = (sum + 4) / 9;
        var blended = (pixels[idx] * 3 + filtered + 2) >> 2;
        pixels[idx] = (short)Math.Clamp(blended, 0, maxVal);
      }
    }
  }

  private static int _FloorLog2(int n) {
    var s = 0;
    while ((1 << (s + 1)) <= n)
      ++s;
    return s;
  }
}
