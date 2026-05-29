using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Bpg.Codec;

/// <summary>HEVC intra prediction: 35 modes (DC, Planar, 33 angular) for block sizes 4x4 through 32x32.</summary>
internal static class HevcIntraPredictor {

  /// <summary>Intra prediction mode indices.</summary>
  public const int Planar = 0;
  public const int Dc = 1;
  public const int Angular2 = 2;  // Start of angular modes
  public const int Angular34 = 34; // End of angular modes

  // Angular mode displacement table (intraPredAngle for modes 2..34)
  // Indexed by [mode - 2], provides the displacement in 1/32 pixel units
  private static readonly int[] _IntraPredAngle = [
    32, 26, 21, 17, 13, 9, 5, 2, 0, -2, -5, -9, -13, -17, -21, -26,
    -32, -26, -21, -17, -13, -9, -5, -2, 0, 2, 5, 9, 13, 17, 21, 26, 32,
  ];

  // Inverse angle table for modes where the angle is negative
  private static readonly int[] _InvAngle = [
    -4096, -1638, -910, -630, -482, -390, -315, -256,
    -315, -390, -482, -630, -910, -1638, -4096,
  ];

  /// <summary>Performs intra prediction for a block.</summary>
  /// <param name="mode">Intra prediction mode (0 = Planar, 1 = DC, 2..34 = Angular).</param>
  /// <param name="dst">Destination buffer for the predicted block (row-major, size*size samples).</param>
  /// <param name="refAbove">Reference samples above the block (2*size+1 samples, index 0 = top-left corner).</param>
  /// <param name="refLeft">Reference samples to the left of the block (2*size+1 samples, index 0 = top-left corner).</param>
  /// <param name="size">Block size (4, 8, 16, or 32).</param>
  /// <param name="bitDepth">Bit depth (8 or 10).</param>
  public static void Predict(int mode, int[] dst, int[] refAbove, int[] refLeft, int size, int bitDepth) {
    switch (mode) {
      case Planar:
        _PredictPlanar(dst, refAbove, refLeft, size);
        break;
      case Dc:
        _PredictDc(dst, refAbove, refLeft, size, bitDepth);
        break;
      default:
        if (mode is >= Angular2 and <= Angular34)
          _PredictAngular(dst, refAbove, refLeft, size, mode, bitDepth);
        else
          throw new NotSupportedException($"Intra prediction mode {mode} is not supported.");
        break;
    }
  }

  private static void _PredictPlanar(int[] dst, int[] refAbove, int[] refLeft, int size) {
    // Planar: weighted bilinear interpolation using top, left, top-right, and bottom-left references
    var topRight = refAbove[size + 1];
    var bottomLeft = refLeft[size + 1];
    var log2Size = _Log2(size);

    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var horPred = (size - 1 - x) * refLeft[y + 1] + (x + 1) * topRight;
        var verPred = (size - 1 - y) * refAbove[x + 1] + (y + 1) * bottomLeft;
        dst[y * size + x] = (horPred + verPred + size) >> (log2Size + 1);
      }
  }

  private static void _PredictDc(int[] dst, int[] refAbove, int[] refLeft, int size, int bitDepth) {
    var sum = 0;
    for (var i = 0; i < size; ++i) {
      sum += refAbove[i + 1];
      sum += refLeft[i + 1];
    }

    var dcVal = (sum + size) >> (_Log2(size) + 1);
    var maxVal = (1 << bitDepth) - 1;
    dcVal = Math.Clamp(dcVal, 0, maxVal);

    // Fill block with DC value
    Array.Fill(dst, dcVal, 0, size * size);

    // Apply DC filtering on top and left edges (for sizes <= 32)
    if (size <= 32) {
      // Top-left corner
      dst[0] = (refAbove[1] + refLeft[1] + 2 * dcVal + 2) >> 2;

      // Top edge
      for (var x = 1; x < size; ++x)
        dst[x] = (refAbove[x + 1] + 3 * dcVal + 2) >> 2;

      // Left edge
      for (var y = 1; y < size; ++y)
        dst[y * size] = (refLeft[y + 1] + 3 * dcVal + 2) >> 2;
    }
  }

  private static void _PredictAngular(int[] dst, int[] refAbove, int[] refLeft, int size, int mode, int bitDepth) {
    var angle = _IntraPredAngle[mode - 2];
    var isHorizontal = mode >= 18; // Modes 18..34 are horizontal-like
    var maxVal = (1 << bitDepth) - 1;

    // Select main and side reference arrays
    int[] refMain, refSide;
    if (isHorizontal) {
      refMain = refLeft;
      refSide = refAbove;
    } else {
      refMain = refAbove;
      refSide = refLeft;
    }

    // For negative angles, extend the reference with projected side samples
    var refMainExtended = refMain;
    if (angle < 0) {
      refMainExtended = new int[2 * size + 1 + size];
      Array.Copy(refMain, 0, refMainExtended, size, 2 * size + 1);

      var invAngleIdx = mode < 18 ? mode - 11 : mode - 25;
      if (invAngleIdx < 0 || invAngleIdx >= _InvAngle.Length)
        invAngleIdx = Math.Clamp(invAngleIdx, 0, _InvAngle.Length - 1);

      var invAngle = _InvAngle[invAngleIdx];
      var invAngleSum = 128;
      for (var i = -1; i >= -size; --i) {
        invAngleSum += invAngle;
        var refIdx = (invAngleSum >> 8);
        if (refIdx >= 0 && refIdx < refSide.Length)
          refMainExtended[size + i] = refSide[refIdx];
        else
          refMainExtended[size + i] = refSide[Math.Clamp(refIdx, 0, refSide.Length - 1)];
      }

      // Shift base to align index 0 with the origin
      var shifted = new int[3 * size + 2];
      Array.Copy(refMainExtended, 0, shifted, 0, Math.Min(refMainExtended.Length, shifted.Length));
      refMainExtended = shifted;
    }

    // Generate predicted samples
    var baseOffset = angle < 0 ? size : 0;
    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        int c, r;
        if (isHorizontal) {
          c = y;
          r = x;
        } else {
          c = x;
          r = y;
        }

        var deltaPos = (r + 1) * angle;
        var deltaInt = deltaPos >> 5;
        var deltaFrac = deltaPos & 31;

        var refOffset = baseOffset + c + 1 + deltaInt;
        if (refOffset < 0 || refOffset + 1 >= refMainExtended.Length) {
          dst[y * size + x] = Math.Clamp(refMainExtended[Math.Clamp(refOffset, 0, refMainExtended.Length - 1)], 0, maxVal);
          continue;
        }

        if (deltaFrac != 0) {
          var val = ((32 - deltaFrac) * refMainExtended[refOffset] + deltaFrac * refMainExtended[refOffset + 1] + 16) >> 5;
          dst[y * size + x] = Math.Clamp(val, 0, maxVal);
        } else {
          dst[y * size + x] = Math.Clamp(refMainExtended[refOffset], 0, maxVal);
        }
      }

    // Post-processing: apply strong-intra-smoothing-style filtering on first row/column for mode 10 (pure horizontal) or mode 26 (pure vertical)
    if (mode == 10 && size < 32)
      for (var y = 0; y < size; ++y)
        dst[y * size] = Math.Clamp(refLeft[y + 1], 0, maxVal);

    if (mode == 26 && size < 32)
      for (var x = 0; x < size; ++x)
        dst[x] = Math.Clamp(refAbove[x + 1], 0, maxVal);
  }

  /// <summary>Generates reference samples for a block at (x, y) in the frame.</summary>
  /// <param name="refAbove">Output: 2*size+1 samples above the block.</param>
  /// <param name="refLeft">Output: 2*size+1 samples to the left of the block.</param>
  /// <param name="frame">Frame sample buffer for the given plane.</param>
  /// <param name="stride">Row stride.</param>
  /// <param name="x">Block X position in samples.</param>
  /// <param name="y">Block Y position in samples.</param>
  /// <param name="size">Block size.</param>
  /// <param name="frameWidth">Frame width in samples.</param>
  /// <param name="frameHeight">Frame height in samples.</param>
  /// <param name="bitDepth">Bit depth.</param>
  public static void BuildReferenceArrays(
    int[] refAbove, int[] refLeft,
    int[] frame, int stride,
    int x, int y, int size,
    int frameWidth, int frameHeight, int bitDepth
  ) {
    var dcDefault = 1 << (bitDepth - 1);
    var refSize = 2 * size + 1;

    // Check availability of neighbors
    var topAvailable = y > 0;
    var leftAvailable = x > 0;
    var topLeftAvailable = topAvailable && leftAvailable;

    if (!topAvailable && !leftAvailable) {
      // No neighbors: fill with DC default
      Array.Fill(refAbove, dcDefault, 0, refSize);
      Array.Fill(refLeft, dcDefault, 0, refSize);
      return;
    }

    // Top-left corner sample
    if (topLeftAvailable)
      refAbove[0] = refLeft[0] = frame[(y - 1) * stride + (x - 1)];
    else if (topAvailable)
      refAbove[0] = refLeft[0] = frame[(y - 1) * stride + x];
    else
      refAbove[0] = refLeft[0] = frame[y * stride + (x - 1)];

    // Fill above reference (indices 1..2*size)
    if (topAvailable) {
      for (var i = 0; i < 2 * size; ++i) {
        var sx = x + i;
        if (sx < frameWidth)
          refAbove[i + 1] = frame[(y - 1) * stride + sx];
        else
          refAbove[i + 1] = refAbove[i]; // Repeat last available
      }
    } else {
      Array.Fill(refAbove, refAbove[0], 1, 2 * size);
    }

    // Fill left reference (indices 1..2*size)
    if (leftAvailable) {
      for (var i = 0; i < 2 * size; ++i) {
        var sy = y + i;
        if (sy < frameHeight)
          refLeft[i + 1] = frame[sy * stride + (x - 1)];
        else
          refLeft[i + 1] = refLeft[i]; // Repeat last available
      }
    } else {
      Array.Fill(refLeft, refLeft[0], 1, 2 * size);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Log2(int n) {
    var log = 0;
    var v = n;
    while (v > 1) {
      v >>= 1;
      ++log;
    }
    return log;
  }
}
