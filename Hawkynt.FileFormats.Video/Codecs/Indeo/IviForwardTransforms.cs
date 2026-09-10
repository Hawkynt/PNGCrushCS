using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>Forward forms of the slant transforms Indeo 5's basic profile decodes.</summary>
/// <remarks>
/// Intel's inverse transform is an integer approximation built from butterflies and two small
/// reflections. Running those stages backwards gives coefficients whose inverse differs only by the
/// rounding the original transform itself introduces. The reflection inverses below come from the
/// two rational matrices: 20/29, 8/29 for the common reflection and 32/65, 56/65 for the first
/// eight-point stage. They are derived from the decoder's arithmetic rather than copied from an
/// external implementation.
/// </remarks>
internal static class IviForwardTransforms {

  internal static void Slant8x8(ReadOnlySpan<int> samples, Span<int> coefficients) {
    if (samples.Length < 64 || coefficients.Length < 64)
      throw new ArgumentException("An eight-by-eight slant transform needs sixty-four values.");

    Span<int> intermediate = stackalloc int[64];
    Span<int> source = stackalloc int[8];
    Span<int> target = stackalloc int[8];

    // IviTransforms halves only after the second dimension. Double the requested samples before
    // undoing the row kernel so that the reconstructed picture lands on the requested sample grid.
    for (var row = 0; row < 8; ++row) {
      for (var column = 0; column < 8; ++column)
        source[column] = samples[row * 8 + column] << 1;

      _Slant8(source, target);
      target.CopyTo(intermediate[(row * 8)..]);
    }

    for (var column = 0; column < 8; ++column) {
      for (var row = 0; row < 8; ++row)
        source[row] = intermediate[row * 8 + column];

      _Slant8(source, target);
      for (var row = 0; row < 8; ++row)
        coefficients[row * 8 + column] = target[row];
    }
  }

  internal static void Slant4x4(ReadOnlySpan<int> samples, Span<int> coefficients) {
    if (samples.Length < 16 || coefficients.Length < 16)
      throw new ArgumentException("A four-by-four slant transform needs sixteen values.");

    Span<int> intermediate = stackalloc int[16];
    Span<int> source = stackalloc int[4];
    Span<int> target = stackalloc int[4];

    for (var row = 0; row < 4; ++row) {
      for (var column = 0; column < 4; ++column)
        source[column] = samples[row * 4 + column] << 1;

      _Slant4(source, target);
      target.CopyTo(intermediate[(row * 4)..]);
    }

    for (var column = 0; column < 4; ++column) {
      for (var row = 0; row < 4; ++row)
        source[row] = intermediate[row * 4 + column];

      _Slant4(source, target);
      for (var row = 0; row < 4; ++row)
        coefficients[row * 4 + column] = target[row];
    }
  }

  /// <summary>Reverses <c>IviTransforms._Slant8</c>.</summary>
  private static void _Slant8(ReadOnlySpan<int> transformed, Span<int> source) {
    int t1 = transformed[0], t2 = transformed[1], t3 = transformed[2], t4 = transformed[3];
    int t5 = transformed[4], t6 = transformed[5], t7 = transformed[6], t8 = transformed[7];

    (t1, t4) = _UndoButterfly(t1, t4);
    (t2, t3) = _UndoButterfly(t2, t3);
    (t5, t8) = _UndoButterfly(t5, t8);
    (t6, t7) = _UndoButterfly(t6, t7);

    (t4, t3) = _UndoReflection(t4, t3);
    (t8, t7) = _UndoReflection(t8, t7);
    (t1, t2) = _UndoButterfly(t1, t2);
    (t5, t6) = _UndoButterfly(t5, t6);

    var (s1, firstT5) = _UndoButterfly(t1, t5);
    var (s2, s6) = _UndoButterfly(t2, t6);
    var (s7, s3) = _UndoButterfly(t7, t3);
    var (firstT4, s8) = _UndoButterfly(t4, t8);

    // The opening reflection of the inverse eight-point kernel is
    // [ 1/2  7/8; 7/8 -1/2 ]. Its inverse is 64/65 times that matrix.
    var s4 = _RoundDivide(32 * firstT4 + 56 * firstT5, 65);
    var s5 = _RoundDivide(56 * firstT4 - 32 * firstT5, 65);

    source[0] = s1;
    source[1] = s4;
    source[2] = s8;
    source[3] = s5;
    source[4] = s2;
    source[5] = s6;
    source[6] = s3;
    source[7] = s7;
  }

  /// <summary>Reverses <c>IviTransforms._Slant4</c>.</summary>
  private static void _Slant4(ReadOnlySpan<int> transformed, Span<int> source) {
    var (t1, t4) = _UndoButterfly(transformed[0], transformed[3]);
    var (t2, t3) = _UndoButterfly(transformed[1], transformed[2]);
    var (s1, s2) = _UndoButterfly(t1, t2);
    var (s4, s3) = _UndoReflection(t4, t3);

    source[0] = s1;
    source[1] = s4;
    source[2] = s2;
    source[3] = s3;
  }

  private static (int First, int Second) _UndoButterfly(int sum, int difference)
    => (_RoundDivide(sum + difference, 2), _RoundDivide(sum - difference, 2));

  /// <summary>
  /// Reverses the inverse kernel's reflection by multiplying by
  /// <c>[20/29 8/29; 8/29 -20/29]</c> and rounding to the nearest integer.
  /// </summary>
  private static (int First, int Second) _UndoReflection(int first, int second)
    => (_RoundDivide(20 * first + 8 * second, 29), _RoundDivide(8 * first - 20 * second, 29));

  private static int _RoundDivide(int value, int divisor)
    => value >= 0 ? (value + (divisor >> 1)) / divisor : -((-value + (divisor >> 1)) / divisor);
}
