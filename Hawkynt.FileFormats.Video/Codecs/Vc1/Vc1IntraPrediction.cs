using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// The quantised coefficients each block leaves behind for its neighbours to predict from
/// (8.1.3.2, 8.1.3.7).
/// </summary>
/// <remarks>
/// Prediction in VC-1 happens on quantised coefficients, before inverse quantisation and before the
/// transform, so what a block has to keep for its neighbours is not pixels: it is the DC, the seven AC
/// coefficients along its top edge and the seven down its left edge. Kept per block of a plane rather
/// than per macroblock, because a macroblock's four luma blocks predict from each other as readily as
/// from the macroblock next door.
/// </remarks>
internal sealed class Vc1IntraPrediction {

  private readonly int[] _dc;
  private readonly int[] _top;
  private readonly int[] _left;
  private readonly int _width;

  internal Vc1IntraPrediction(int blocksWide, int blocksHigh) {
    this._width = blocksWide;
    this._dc = new int[blocksWide * blocksHigh];
    this._top = new int[blocksWide * blocksHigh * 7];
    this._left = new int[blocksWide * blocksHigh * 7];
  }

  /// <summary>Keeps what one block owes its neighbours, from its fully reconstructed quantised block.</summary>
  internal void Store(int column, int row, ReadOnlySpan<int> block) {
    var index = (row * this._width) + column;
    this._dc[index] = block[0];

    var at = index * 7;
    for (var i = 0; i < 7; ++i) {
      this._top[at + i] = block[i + 1];
      this._left[at + i] = block[(i + 1) * 8];
    }
  }

  internal int Dc(int column, int row) => this._dc[(row * this._width) + column];

  /// <summary>The seven AC coefficients along a block's top edge.</summary>
  internal ReadOnlySpan<int> Top(int column, int row) => this._top.AsSpan(((row * this._width) + column) * 7, 7);

  /// <summary>The seven AC coefficients down a block's left edge.</summary>
  internal ReadOnlySpan<int> Left(int column, int row) => this._left.AsSpan(((row * this._width) + column) * 7, 7);

  /// <summary>
  /// Picks the DC predictor and the direction the AC prediction will follow (Figure 39).
  /// </summary>
  /// <remarks>
  /// Three candidates — above, above-left and left — and the rule picks whichever of the two edges the
  /// third suggests is the smoother. A candidate outside the picture is not skipped but replaced by a
  /// default, so the comparison is always between three numbers; leaving one out would change which
  /// direction wins along every edge of the picture.
  /// </remarks>
  internal (int Predictor, bool FromTop) Predict(int column, int row, int defaultPredictor) {
    var above = row > 0 ? this.Dc(column, row - 1) : defaultPredictor;
    var aboveLeft = row > 0 && column > 0 ? this.Dc(column - 1, row - 1) : defaultPredictor;
    var left = column > 0 ? this.Dc(column - 1, row) : defaultPredictor;

    return Math.Abs(aboveLeft - above) <= Math.Abs(aboveLeft - left)
      ? (left, false)
      : (above, true);
  }
}
