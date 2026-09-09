using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Turns a block of dequantised coefficients back into samples.
/// </summary>
/// <param name="coefficients">The block's coefficients in natural order, sixty-four or sixteen of them.</param>
/// <param name="destination">The band buffer to write the samples into.</param>
/// <param name="offset">Where in that buffer the block's first sample goes.</param>
/// <param name="pitch">How far apart two rows of the band buffer are.</param>
/// <param name="columnFlags">
/// One flag per column, saying whether any coefficient in it was non-zero. The column pass skips a
/// column that has none, which is not merely an optimisation: it is how the transform is defined.
/// </param>
internal delegate void IviInverseTransform(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags);

/// <summary>
/// Fills a block from its DC coefficient alone, which is what an uncoded intra block reconstructs to.
/// </summary>
/// <remarks>
/// This is not the full transform applied to a block whose only non-zero coefficient is the DC one.
/// It rounds differently and, for the one-dimensional transforms, it fills only the row or column the
/// transform runs along and leaves the rest at zero — because a one-dimensional transform of a single
/// DC coefficient is exactly that.
/// </remarks>
internal delegate void IviDcTransform(int dc, short[] destination, int offset, int pitch, int blockSize);

/// <summary>
/// The inverse transforms of Indeo 4 and Indeo 5: the slant transform, the Haar transform, and the
/// pass-through that is neither.
/// </summary>
/// <remarks>
/// Both formats transform a block in two passes — down the columns, then along the rows — and both
/// leave the round-off entirely to the second pass. The slant transform's row pass halves with a
/// rounding add and its column pass does not halve at all; the Haar transform halves inside every
/// butterfly and rounds nowhere. A decoder that put the rounding in the other pass, or that rounded
/// symmetrically, would be one off here and there, and one off in a reference frame is the error the
/// next frame predicts from.
/// <para/>
/// Both formats also allow a band to be transformed in <i>one</i> dimension only, along rows or down
/// columns, and Indeo 4 additionally allows a band whose samples are stored directly. Those are not
/// degenerate cases of the two-dimensional transform: they use their own scan patterns, their own DC
/// reconstruction, and the one-dimensional forms skip the pass they do not run rather than running it
/// with an identity.
/// <para/>
/// Intermediates are held to thirty-two bits and each output to sixteen, wrapping rather than
/// clamping, which is what the fixed-point arithmetic of the original decoders did and therefore what
/// the encoder assumed.
/// </remarks>
internal static class IviTransforms {

  // ============================================================================================
  // Slant
  // ============================================================================================

  internal static void InverseSlant8x8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> intermediate = stackalloc int[64];
    Span<int> line = stackalloc int[8];
    Span<int> transformed = stackalloc int[8];

    for (var column = 0; column < 8; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 8; ++row)
          intermediate[row * 8 + column] = 0;

        continue;
      }

      for (var row = 0; row < 8; ++row)
        line[row] = coefficients[row * 8 + column];

      _Slant8(line, transformed);

      for (var row = 0; row < 8; ++row)
        intermediate[row * 8 + column] = transformed[row];
    }

    for (var row = 0; row < 8; ++row) {
      var at = offset + row * pitch;
      var source = intermediate.Slice(row * 8, 8);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 8).Clear();
        continue;
      }

      _Slant8(source, transformed);

      for (var column = 0; column < 8; ++column)
        destination[at + column] = (short)((transformed[column] + 1) >> 1);
    }
  }

  internal static void InverseSlant4x4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> intermediate = stackalloc int[16];
    Span<int> line = stackalloc int[4];
    Span<int> transformed = stackalloc int[4];

    for (var column = 0; column < 4; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 4; ++row)
          intermediate[row * 4 + column] = 0;

        continue;
      }

      for (var row = 0; row < 4; ++row)
        line[row] = coefficients[row * 4 + column];

      _Slant4(line, transformed);

      for (var row = 0; row < 4; ++row)
        intermediate[row * 4 + column] = transformed[row];
    }

    for (var row = 0; row < 4; ++row) {
      var at = offset + row * pitch;
      var source = intermediate.Slice(row * 4, 4);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 4).Clear();
        continue;
      }

      _Slant4(source, transformed);

      for (var column = 0; column < 4; ++column)
        destination[at + column] = (short)((transformed[column] + 1) >> 1);
    }
  }

  internal static void RowSlant8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> transformed = stackalloc int[8];

    for (var row = 0; row < 8; ++row) {
      var at = offset + row * pitch;
      var source = coefficients.AsSpan(row * 8, 8);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 8).Clear();
        continue;
      }

      _Slant8(source, transformed);

      for (var column = 0; column < 8; ++column)
        destination[at + column] = (short)((transformed[column] + 1) >> 1);
    }
  }

  internal static void RowSlant4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> transformed = stackalloc int[4];

    for (var row = 0; row < 4; ++row) {
      var at = offset + row * pitch;
      var source = coefficients.AsSpan(row * 4, 4);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 4).Clear();
        continue;
      }

      _Slant4(source, transformed);

      for (var column = 0; column < 4; ++column)
        destination[at + column] = (short)((transformed[column] + 1) >> 1);
    }
  }

  internal static void ColumnSlant8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> line = stackalloc int[8];
    Span<int> transformed = stackalloc int[8];

    for (var column = 0; column < 8; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 8; ++row)
          destination[offset + column + row * pitch] = 0;

        continue;
      }

      for (var row = 0; row < 8; ++row)
        line[row] = coefficients[row * 8 + column];

      _Slant8(line, transformed);

      for (var row = 0; row < 8; ++row)
        destination[offset + column + row * pitch] = (short)((transformed[row] + 1) >> 1);
    }
  }

  internal static void ColumnSlant4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> line = stackalloc int[4];
    Span<int> transformed = stackalloc int[4];

    for (var column = 0; column < 4; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 4; ++row)
          destination[offset + column + row * pitch] = 0;

        continue;
      }

      for (var row = 0; row < 4; ++row)
        line[row] = coefficients[row * 4 + column];

      _Slant4(line, transformed);

      for (var row = 0; row < 4; ++row)
        destination[offset + column + row * pitch] = (short)((transformed[row] + 1) >> 1);
    }
  }

  internal static void DcSlant2D(int dc, short[] destination, int offset, int pitch, int blockSize) {
    var value = (short)((dc + 1) >> 1);

    for (var row = 0; row < blockSize; ++row)
      destination.AsSpan(offset + row * pitch, blockSize).Fill(value);
  }

  internal static void DcRowSlant(int dc, short[] destination, int offset, int pitch, int blockSize) {
    destination.AsSpan(offset, blockSize).Fill((short)((dc + 1) >> 1));

    for (var row = 1; row < blockSize; ++row)
      destination.AsSpan(offset + row * pitch, blockSize).Clear();
  }

  internal static void DcColumnSlant(int dc, short[] destination, int offset, int pitch, int blockSize) {
    var value = (short)((dc + 1) >> 1);

    for (var row = 0; row < blockSize; ++row) {
      var at = offset + row * pitch;
      destination[at] = value;
      destination.AsSpan(at + 1, blockSize - 1).Clear();
    }
  }

  // ============================================================================================
  // Haar
  // ============================================================================================

  internal static void InverseHaar8x8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> intermediate = stackalloc int[64];
    Span<int> line = stackalloc int[8];
    Span<int> transformed = stackalloc int[8];

    for (var column = 0; column < 8; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 8; ++row)
          intermediate[row * 8 + column] = 0;

        continue;
      }

      // The top half of every column is pre-scaled by two. That asymmetry is the transform's, not a
      // normalisation that could be folded into the quantiser.
      var scale = (column & 4) == 0 ? 1 : 0;
      for (var row = 0; row < 4; ++row)
        line[row] = coefficients[row * 8 + column] * (1 << scale);

      for (var row = 4; row < 8; ++row)
        line[row] = coefficients[row * 8 + column];

      _Haar8(line, transformed);

      for (var row = 0; row < 8; ++row)
        intermediate[row * 8 + column] = transformed[row];
    }

    for (var row = 0; row < 8; ++row) {
      var at = offset + row * pitch;
      var source = intermediate.Slice(row * 8, 8);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 8).Clear();
        continue;
      }

      _Haar8(source, transformed);

      for (var column = 0; column < 8; ++column)
        destination[at + column] = (short)transformed[column];
    }
  }

  internal static void InverseHaar4x4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> intermediate = stackalloc int[16];
    Span<int> line = stackalloc int[4];
    Span<int> transformed = stackalloc int[4];

    for (var column = 0; column < 4; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 4; ++row)
          intermediate[row * 4 + column] = 0;

        continue;
      }

      var scale = (column & 2) == 0 ? 1 : 0;
      line[0] = coefficients[column] * (1 << scale);
      line[1] = coefficients[4 + column] * (1 << scale);
      line[2] = coefficients[8 + column];
      line[3] = coefficients[12 + column];

      _Haar4(line, transformed);

      for (var row = 0; row < 4; ++row)
        intermediate[row * 4 + column] = transformed[row];
    }

    for (var row = 0; row < 4; ++row) {
      var at = offset + row * pitch;
      var source = intermediate.Slice(row * 4, 4);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 4).Clear();
        continue;
      }

      _Haar4(source, transformed);

      for (var column = 0; column < 4; ++column)
        destination[at + column] = (short)transformed[column];
    }
  }

  internal static void RowHaar8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> transformed = stackalloc int[8];

    for (var row = 0; row < 8; ++row) {
      var at = offset + row * pitch;
      var source = coefficients.AsSpan(row * 8, 8);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 8).Clear();
        continue;
      }

      _Haar8(source, transformed);

      for (var column = 0; column < 8; ++column)
        destination[at + column] = (short)transformed[column];
    }
  }

  internal static void RowHaar4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> transformed = stackalloc int[4];

    for (var row = 0; row < 4; ++row) {
      var at = offset + row * pitch;
      var source = coefficients.AsSpan(row * 4, 4);

      if (_IsAllZero(source)) {
        destination.AsSpan(at, 4).Clear();
        continue;
      }

      _Haar4(source, transformed);

      for (var column = 0; column < 4; ++column)
        destination[at + column] = (short)transformed[column];
    }
  }

  internal static void ColumnHaar8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> line = stackalloc int[8];
    Span<int> transformed = stackalloc int[8];

    for (var column = 0; column < 8; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 8; ++row)
          destination[offset + column + row * pitch] = 0;

        continue;
      }

      for (var row = 0; row < 8; ++row)
        line[row] = coefficients[row * 8 + column];

      _Haar8(line, transformed);

      for (var row = 0; row < 8; ++row)
        destination[offset + column + row * pitch] = (short)transformed[row];
    }
  }

  internal static void ColumnHaar4(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    Span<int> line = stackalloc int[4];
    Span<int> transformed = stackalloc int[4];

    for (var column = 0; column < 4; ++column) {
      if (columnFlags[column] == 0) {
        for (var row = 0; row < 4; ++row)
          destination[offset + column + row * pitch] = 0;

        continue;
      }

      for (var row = 0; row < 4; ++row)
        line[row] = coefficients[row * 4 + column];

      _Haar4(line, transformed);

      for (var row = 0; row < 4; ++row)
        destination[offset + column + row * pitch] = (short)transformed[row];
    }
  }

  internal static void DcHaar2D(int dc, short[] destination, int offset, int pitch, int blockSize) {
    var value = (short)(dc >> 3);

    for (var row = 0; row < blockSize; ++row)
      destination.AsSpan(offset + row * pitch, blockSize).Fill(value);
  }

  // ============================================================================================
  // No transform at all
  // ============================================================================================

  /// <summary>Stores the block's values as samples, for a band that codes samples directly.</summary>
  internal static void PutPixels8x8(int[] coefficients, short[] destination, int offset, int pitch, byte[] columnFlags) {
    for (var row = 0; row < 8; ++row) {
      var at = offset + row * pitch;
      for (var column = 0; column < 8; ++column)
        destination[at + column] = (short)coefficients[row * 8 + column];
    }
  }

  /// <summary>
  /// Stores one sample and leaves the other sixty-three at zero.
  /// </summary>
  /// <remarks>
  /// Always eight by eight, whatever block size the band states: the band that codes samples directly
  /// is an eight-by-eight one, and the size argument is what the other DC reconstructions need rather
  /// than something this one honours.
  /// </remarks>
  internal static void PutDcPixel8x8(int dc, short[] destination, int offset, int pitch, int blockSize) {
    destination[offset] = (short)dc;
    destination.AsSpan(offset + 1, 7).Clear();

    for (var row = 1; row < 8; ++row)
      destination.AsSpan(offset + row * pitch, 8).Clear();
  }

  // ============================================================================================
  // The one-dimensional kernels
  // ============================================================================================

  /// <summary>
  /// The eight-point inverse slant transform, over the eight values of one row or column.
  /// </summary>
  /// <remarks>
  /// The inputs arrive in the frequency order the transform butterflies expect, which is not the
  /// order they sit in the block: what the original decoder calls s1, s4, s8, s5, s2, s6, s3, s7 are
  /// positions zero through seven. Reordering them into ascending frequency would change the
  /// arithmetic, because the reflections below are not symmetric in their two arguments.
  /// </remarks>
  private static void _Slant8(ReadOnlySpan<int> source, Span<int> destination) {
    int s1 = source[0], s4 = source[1], s8 = source[2], s5 = source[3];
    int s2 = source[4], s6 = source[5], s3 = source[6], s7 = source[7];

    // A reflection by 1/2, 7/8.
    var t4 = s5 + ((s4 * 4 - s5 + 4) >> 3);
    var t5 = s4 + ((-s4 - s5 * 4 + 4) >> 3);

    var t1 = s1 + t5;
    t5 = s1 - t5;
    var t2 = s2 + s6;
    var t6 = s2 - s6;
    var t7 = s7 + s3;
    var t3 = s7 - s3;
    var t8 = t4 - s8;
    t4 += s8;

    (t1, t2) = (t1 + t2, t1 - t2);
    (t4, t3) = _Reflect(t4, t3);
    (t5, t6) = (t5 + t6, t5 - t6);
    (t8, t7) = _Reflect(t8, t7);

    (t1, t4) = (t1 + t4, t1 - t4);
    (t2, t3) = (t2 + t3, t2 - t3);
    (t5, t8) = (t5 + t8, t5 - t8);
    (t6, t7) = (t6 + t7, t6 - t7);

    destination[0] = t1;
    destination[1] = t2;
    destination[2] = t3;
    destination[3] = t4;
    destination[4] = t5;
    destination[5] = t6;
    destination[6] = t7;
    destination[7] = t8;
  }

  /// <summary>The four-point inverse slant transform, over one row or column of a four-wide block.</summary>
  private static void _Slant4(ReadOnlySpan<int> source, Span<int> destination) {
    int s1 = source[0], s4 = source[1], s2 = source[2], s3 = source[3];

    var t1 = s1 + s2;
    var t2 = s1 - s2;
    var (t4, t3) = _Reflect(s4, s3);

    destination[0] = t1 + t4;
    destination[3] = t1 - t4;
    destination[1] = t2 + t3;
    destination[2] = t2 - t3;
  }

  /// <summary>A reflection by 1/2, 5/4, which is the slant transform's rotation stage.</summary>
  private static (int First, int Second) _Reflect(int first, int second)
    => (((first + second * 2 + 2) >> 2) + first, ((first * 2 - second + 2) >> 2) - second);

  /// <summary>
  /// The eight-point inverse Haar transform, over the eight values of one row or column.
  /// </summary>
  /// <remarks>
  /// As with the slant transform the inputs are in the butterflies' order and not the block's: what
  /// the original decoder calls s1, s5, s3, s7, s2, s4, s6, s8 are positions zero through seven. Each
  /// butterfly halves, rounding towards negative infinity, and nothing rounds afterwards.
  /// </remarks>
  private static void _Haar8(ReadOnlySpan<int> source, Span<int> destination) {
    int s1 = source[0], s5 = source[1], s3 = source[2], s7 = source[3];
    int s2 = source[4], s4 = source[5], s6 = source[6], s8 = source[7];

    var (t1, t5) = _HaarButterfly(s1 * 2, s5 * 2);
    int t2, t3, t4, t6, t7, t8;

    (t1, t3) = _HaarButterfly(t1, s3);
    (t5, t7) = _HaarButterfly(t5, s7);
    (t1, t2) = _HaarButterfly(t1, s2);
    (t3, t4) = _HaarButterfly(t3, s4);
    (t5, t6) = _HaarButterfly(t5, s6);
    (t7, t8) = _HaarButterfly(t7, s8);

    destination[0] = t1;
    destination[1] = t2;
    destination[2] = t3;
    destination[3] = t4;
    destination[4] = t5;
    destination[5] = t6;
    destination[6] = t7;
    destination[7] = t8;
  }

  /// <summary>The four-point inverse Haar transform.</summary>
  private static void _Haar4(ReadOnlySpan<int> source, Span<int> destination) {
    int s1 = source[0], s3 = source[1], s5 = source[2], s7 = source[3];

    var (low, high) = _HaarButterfly(s1, s3);
    (destination[0], destination[1]) = _HaarButterfly(low, s5);
    (destination[2], destination[3]) = _HaarButterfly(high, s7);
  }

  private static (int Sum, int Difference) _HaarButterfly(int first, int second)
    => ((first + second) >> 1, (first - second) >> 1);

  private static bool _IsAllZero(ReadOnlySpan<int> values) {
    foreach (var value in values)
      if (value != 0)
        return false;

    return true;
  }
}
