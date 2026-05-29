using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Photo Core Transform (PCT) for JPEG XR -- a 4x4 integer transform
/// plus overlap pre/post-filtering for deblocking.
/// Based on ITU-T T.832 / ISO 29199-2 specification.
/// </summary>
internal static class JxrTransform {

  /// <summary>
  /// Forward Photo Core Transform on a 4x4 block.
  /// Stage 1: 2x2 Hadamard on each quadrant
  /// Stage 2: Rotation operations between quadrants
  /// Input/output: 16-element array in row-major order.
  /// </summary>
  public static void ForwardPCT(Span<int> block) {
    if (block.Length < 16)
      throw new ArgumentException("Block must have at least 16 elements.", nameof(block));

    // Stage 1: Apply 2x2 Hadamard to each 2x2 quadrant
    // Top-left quadrant: [0,1,4,5]
    _Hadamard2x2(ref block[0], ref block[1], ref block[4], ref block[5]);
    // Top-right quadrant: [2,3,6,7]
    _Hadamard2x2(ref block[2], ref block[3], ref block[6], ref block[7]);
    // Bottom-left quadrant: [8,9,12,13]
    _Hadamard2x2(ref block[8], ref block[9], ref block[12], ref block[13]);
    // Bottom-right quadrant: [10,11,14,15]
    _Hadamard2x2(ref block[10], ref block[11], ref block[14], ref block[15]);

    // Stage 2: Rotation operations between quadrants
    // Rotate top-right and bottom-left into the transform domain
    _ForwardRotate(ref block[1], ref block[2]);
    _ForwardRotate(ref block[4], ref block[8]);
    _ForwardRotate(ref block[5], ref block[10]);
    _ForwardRotate(ref block[3], ref block[6]);
    _ForwardRotate(ref block[9], ref block[12]);
    _ForwardRotate(ref block[7], ref block[14]);
  }

  /// <summary>
  /// Inverse Photo Core Transform on a 4x4 block.
  /// Reverses Stage 2 rotations, then Stage 1 Hadamard.
  /// </summary>
  public static void InversePCT(Span<int> block) {
    if (block.Length < 16)
      throw new ArgumentException("Block must have at least 16 elements.", nameof(block));

    // Reverse Stage 2: Inverse rotations
    _InverseRotate(ref block[7], ref block[14]);
    _InverseRotate(ref block[9], ref block[12]);
    _InverseRotate(ref block[3], ref block[6]);
    _InverseRotate(ref block[5], ref block[10]);
    _InverseRotate(ref block[4], ref block[8]);
    _InverseRotate(ref block[1], ref block[2]);

    // Reverse Stage 1: Inverse 2x2 Hadamard on each quadrant
    _InverseHadamard2x2(ref block[10], ref block[11], ref block[14], ref block[15]);
    _InverseHadamard2x2(ref block[8], ref block[9], ref block[12], ref block[13]);
    _InverseHadamard2x2(ref block[2], ref block[3], ref block[6], ref block[7]);
    _InverseHadamard2x2(ref block[0], ref block[1], ref block[4], ref block[5]);
  }

  /// <summary>
  /// Forward 2x2 integer-to-integer Hadamard using lifting (S-transform).
  /// Perfectly invertible with no rounding loss.
  /// Applied to rows then columns of a 2x2 block.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _Hadamard2x2(ref int a, ref int b, ref int c, ref int d) {
    // Row lifting: (a,b) -> (s,d) where s=b+(a-b)/2, d=a-b
    var d0 = a - b;
    var s0 = b + (d0 >> 1);
    var d1 = c - d;
    var s1 = d + (d1 >> 1);

    // Column lifting on (s0,s1) and (d0,d1)
    var dc = s0 - s1;
    a = s1 + (dc >> 1); // DC: average of averages
    c = dc;             // vertical difference of averages
    var dd = d0 - d1;
    b = d1 + (dd >> 1); // horizontal difference average
    d = dd;             // diagonal difference
  }

  /// <summary>
  /// Inverse 2x2 integer-to-integer Hadamard (perfectly reverses the forward transform).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _InverseHadamard2x2(ref int a, ref int b, ref int c, ref int d) {
    // Inverse column lifting on (a,c) and (b,d)
    var s1 = a - (c >> 1);
    var s0 = c + s1;
    var d1 = b - (d >> 1);
    var d0 = d + d1;

    // Inverse row lifting
    b = s0 - (d0 >> 1);
    a = d0 + b;
    d = s1 - (d1 >> 1);
    c = d1 + d;
  }

  /// <summary>
  /// Forward rotation for PCT Stage 2 using lifting steps.
  /// Perfectly invertible integer-to-integer S-transform on a pair.
  /// Output: a' = a - b; b' = b + floor(a'/2)
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _ForwardRotate(ref int a, ref int b) {
    a -= b;
    b += a >> 1;
  }

  /// <summary>Inverse rotation for PCT Stage 2 (perfectly reverses forward).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _InverseRotate(ref int a, ref int b) {
    b -= a >> 1;
    a += b;
  }

  /// <summary>
  /// Forward 4x4 overlap pre-filter applied to block boundaries for deblocking.
  /// This is applied BEFORE the forward PCT at macroblock boundaries.
  /// The filter operates on a 4-element span straddling the block boundary.
  /// </summary>
  public static void ForwardOverlap4(Span<int> data) {
    if (data.Length < 4)
      return;

    // Two-stage lifting-based overlap filter
    // Stage 1: Predict
    data[1] -= (data[0] + data[2] + 1) >> 1;
    data[2] -= (data[1] + data[3] + 1) >> 1;

    // Stage 2: Update
    data[0] += (data[1] + 1) >> 1;
    data[3] += (data[2] + 1) >> 1;
  }

  /// <summary>
  /// Inverse 4-element overlap post-filter for deblocking.
  /// Applied AFTER the inverse PCT at macroblock boundaries.
  /// </summary>
  public static void InverseOverlap4(Span<int> data) {
    if (data.Length < 4)
      return;

    // Reverse Stage 2: Undo update
    data[3] -= (data[2] + 1) >> 1;
    data[0] -= (data[1] + 1) >> 1;

    // Reverse Stage 1: Undo predict
    data[2] += (data[1] + data[3] + 1) >> 1;
    data[1] += (data[0] + data[2] + 1) >> 1;
  }

  /// <summary>
  /// Forward 4x4 overlap pre-filter on a full 4x4 block at a boundary.
  /// Applies the 4-element overlap filter to each row and column.
  /// </summary>
  public static void ForwardOverlap4x4(Span<int> block) {
    if (block.Length < 16)
      return;

    Span<int> row = stackalloc int[4];

    // Filter rows
    for (var r = 0; r < 4; ++r) {
      row[0] = block[r * 4];
      row[1] = block[r * 4 + 1];
      row[2] = block[r * 4 + 2];
      row[3] = block[r * 4 + 3];
      ForwardOverlap4(row);
      block[r * 4] = row[0];
      block[r * 4 + 1] = row[1];
      block[r * 4 + 2] = row[2];
      block[r * 4 + 3] = row[3];
    }

    // Filter columns
    for (var c = 0; c < 4; ++c) {
      row[0] = block[c];
      row[1] = block[4 + c];
      row[2] = block[8 + c];
      row[3] = block[12 + c];
      ForwardOverlap4(row);
      block[c] = row[0];
      block[4 + c] = row[1];
      block[8 + c] = row[2];
      block[12 + c] = row[3];
    }
  }

  /// <summary>
  /// Inverse 4x4 overlap post-filter on a full 4x4 block.
  /// </summary>
  public static void InverseOverlap4x4(Span<int> block) {
    if (block.Length < 16)
      return;

    Span<int> col = stackalloc int[4];

    // Inverse filter columns first (reverse order of forward)
    for (var c = 0; c < 4; ++c) {
      col[0] = block[c];
      col[1] = block[4 + c];
      col[2] = block[8 + c];
      col[3] = block[12 + c];
      InverseOverlap4(col);
      block[c] = col[0];
      block[4 + c] = col[1];
      block[8 + c] = col[2];
      block[12 + c] = col[3];
    }

    // Then inverse filter rows
    Span<int> row = stackalloc int[4];
    for (var r = 0; r < 4; ++r) {
      row[0] = block[r * 4];
      row[1] = block[r * 4 + 1];
      row[2] = block[r * 4 + 2];
      row[3] = block[r * 4 + 3];
      InverseOverlap4(row);
      block[r * 4] = row[0];
      block[r * 4 + 1] = row[1];
      block[r * 4 + 2] = row[2];
      block[r * 4 + 3] = row[3];
    }
  }

  /// <summary>Applies forward PCT to all 4x4 blocks in a macroblock (16x16 pixel region).</summary>
  /// <param name="coefficients">Array of 256 coefficients (16x16 in row-major).</param>
  public static void ForwardMacroblockTransform(Span<int> coefficients) {
    if (coefficients.Length < 256)
      throw new ArgumentException("Macroblock must have 256 coefficients.", nameof(coefficients));

    Span<int> block = stackalloc int[16];

    // Apply forward PCT to each 4x4 block within the 16x16 macroblock
    for (var by = 0; by < 4; ++by)
    for (var bx = 0; bx < 4; ++bx) {
      _ExtractBlock(coefficients, block, bx * 4, by * 4, 16);
      ForwardPCT(block);
      _InsertBlock(coefficients, block, bx * 4, by * 4, 16);
    }
  }

  /// <summary>Applies inverse PCT to all 4x4 blocks in a macroblock.</summary>
  public static void InverseMacroblockTransform(Span<int> coefficients) {
    if (coefficients.Length < 256)
      throw new ArgumentException("Macroblock must have 256 coefficients.", nameof(coefficients));

    Span<int> block = stackalloc int[16];

    for (var by = 0; by < 4; ++by)
    for (var bx = 0; bx < 4; ++bx) {
      _ExtractBlock(coefficients, block, bx * 4, by * 4, 16);
      InversePCT(block);
      _InsertBlock(coefficients, block, bx * 4, by * 4, 16);
    }
  }

  /// <summary>Extracts a 4x4 block from a larger grid into a 16-element span.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _ExtractBlock(ReadOnlySpan<int> source, Span<int> block, int x, int y, int stride) {
    for (var r = 0; r < 4; ++r)
    for (var c = 0; c < 4; ++c)
      block[r * 4 + c] = source[(y + r) * stride + x + c];
  }

  /// <summary>Inserts a 4x4 block back into a larger grid from a 16-element span.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _InsertBlock(Span<int> dest, ReadOnlySpan<int> block, int x, int y, int stride) {
    for (var r = 0; r < 4; ++r)
    for (var c = 0; c < 4; ++c)
      dest[(y + r) * stride + x + c] = block[r * 4 + c];
  }
}

/// <summary>Color space conversion utilities for JPEG XR (YCoCg reversible transform).</summary>
internal static class JxrColorConvert {

  /// <summary>Converts RGB to YCoCg (reversible integer color transform).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void RgbToYCoCg(int r, int g, int b, out int y, out int co, out int cg) {
    co = r - b;
    var t = b + (co >> 1);
    cg = g - t;
    y = t + (cg >> 1);
  }

  /// <summary>Converts YCoCg back to RGB (reversible integer color transform).</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void YCoCgToRgb(int y, int co, int cg, out int r, out int g, out int b) {
    var t = y - (cg >> 1);
    g = t + cg;
    b = t - (co >> 1);
    r = b + co;
  }

  /// <summary>Converts an entire macroblock from RGB to YCoCg. Each channel is a 256-element array.</summary>
  public static void RgbToYCoCg(ReadOnlySpan<int> rChannel, ReadOnlySpan<int> gChannel, ReadOnlySpan<int> bChannel,
    Span<int> yChannel, Span<int> coChannel, Span<int> cgChannel) {
    var count = Math.Min(rChannel.Length, Math.Min(gChannel.Length, bChannel.Length));
    for (var i = 0; i < count; ++i)
      RgbToYCoCg(rChannel[i], gChannel[i], bChannel[i], out yChannel[i], out coChannel[i], out cgChannel[i]);
  }

  /// <summary>Converts an entire macroblock from YCoCg to RGB.</summary>
  public static void YCoCgToRgb(ReadOnlySpan<int> yChannel, ReadOnlySpan<int> coChannel, ReadOnlySpan<int> cgChannel,
    Span<int> rChannel, Span<int> gChannel, Span<int> bChannel) {
    var count = Math.Min(yChannel.Length, Math.Min(coChannel.Length, cgChannel.Length));
    for (var i = 0; i < count; ++i)
      YCoCgToRgb(yChannel[i], coChannel[i], cgChannel[i], out rChannel[i], out gChannel[i], out bChannel[i]);
  }

  /// <summary>Clamps a value to the 0..255 byte range.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
