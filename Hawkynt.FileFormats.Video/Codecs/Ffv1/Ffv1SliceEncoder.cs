using System;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// Codes the samples of one FFV1 slice with either entropy coder, using the same context model and
/// median predictor <see cref="Ffv1SliceDecoder"/> reads (RFC 9043 §3.2 to §3.8).
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/ffv1enc_template.c</c>, copyright (c) 2003-2016 Michael
/// Niedermayer, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// The context of a sample and the median it is predicted from are the decoder's own routines,
/// called on the same <see cref="Ffv1Plane"/> with the same border rules, so the two cannot drift
/// apart. The range coder writes every folded prediction difference directly. Golomb-Rice adds the
/// run mode from RFC 9043: a zero context opens a run, flat samples become run lengths, and only the
/// first nonzero difference after the run needs a symbol.
/// </remarks>
internal sealed class Ffv1SliceEncoder {

  private readonly int[][][] _quantTables;
  private readonly int _sampleBits;
  private readonly int _foldShift;
  private readonly Ffv1SliceDecoder _model;

  internal Ffv1SliceEncoder(Ffv1Parameters parameters) {
    this._quantTables = parameters.QuantTables;
    this._sampleBits = parameters.SampleBits;
    this._foldShift = 32 - parameters.SampleBits;
    this._model = new(parameters);
  }

  /// <summary>Codes every line of a plane with the range coder.</summary>
  internal void EncodePlane(Ffv1RangeEncoder coder, Ffv1Plane plane, byte[][] states, int tableSet) {
    for (var y = 0; y < plane.Height; ++y)
      this.EncodeLine(coder, plane, y, states, tableSet);
  }

  /// <summary>Codes one line with the range coder.</summary>
  internal void EncodeLine(Ffv1RangeEncoder coder, Ffv1Plane plane, int y, byte[][] states, int tableSet) {
    var tables = this._quantTables[tableSet];

    for (var x = 0; x < plane.Width; ++x) {
      var (context, difference) = this._ContextAndDifference(tables, plane, x, y);
      coder.Symbol(states[context], difference, true);
    }
  }

  /// <summary>Codes every line of a plane with Golomb-Rice, carrying run state between lines.</summary>
  internal void EncodePlane(Ffv1GolombEncoder coder, Ffv1Plane plane, Ffv1GolombState[] states, int tableSet) {
    var runIndex = 0;
    for (var y = 0; y < plane.Height; ++y)
      this.EncodeLine(coder, plane, y, states, tableSet, ref runIndex);
  }

  /// <summary>Codes one line with Golomb-Rice and its zero-context run mode.</summary>
  internal void EncodeLine(
    Ffv1GolombEncoder coder, Ffv1Plane plane, int y, Ffv1GolombState[] states, int tableSet, ref int runIndex) {
    var tables = this._quantTables[tableSet];
    var runMode = false;
    var runCount = 0;

    for (var x = 0; x < plane.Width; ++x) {
      var (context, difference) = this._ContextAndDifference(tables, plane, x, y);
      if (context == 0)
        runMode = true;

      if (runMode) {
        if (difference == 0) {
          ++runCount;
          continue;
        }

        while (runCount >= 1 << Ffv1GolombDecoder.Log2Run(runIndex)) {
          runCount -= 1 << Ffv1GolombDecoder.Log2Run(runIndex);
          if (runIndex < Ffv1GolombDecoder.MaximumRunIndex)
            ++runIndex;
          coder.Bit(1);
        }

        var remainderBits = Ffv1GolombDecoder.Log2Run(runIndex);
        coder.Bits(1 + remainderBits, runCount);
        if (runIndex != 0)
          --runIndex;

        runCount = 0;
        runMode = false;
        if (difference > 0)
          --difference;
      }

      coder.Symbol(states[context], difference, this._sampleBits);
    }

    if (!runMode)
      return;

    while (runCount >= 1 << Ffv1GolombDecoder.Log2Run(runIndex)) {
      runCount -= 1 << Ffv1GolombDecoder.Log2Run(runIndex);
      if (runIndex < Ffv1GolombDecoder.MaximumRunIndex)
        ++runIndex;
      coder.Bit(1);
    }

    if (runCount != 0)
      coder.Bit(1);
  }

  private (int Context, int Difference) _ContextAndDifference(int[][] tables, Ffv1Plane plane, int x, int y) {
    var left = plane.At(x - 1, y);
    var top = plane.At(x, y - 1);
    var topLeft = plane.At(x - 1, y - 1);

    var context = Ffv1SliceDecoder.ContextOf(tables, plane, x, y, left, top, topLeft);
    var difference = plane[x, y] - this._model.Predict(left, top, topLeft);

    if (context < 0) {
      context = -context;
      difference = -difference;
    }

    difference = (difference << this._foldShift) >> this._foldShift;
    return (context, difference);
  }
}