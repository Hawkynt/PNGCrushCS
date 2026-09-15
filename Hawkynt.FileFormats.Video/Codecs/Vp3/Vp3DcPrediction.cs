namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Converts between the DC residuals carried by the bitstream and the quantised DC coefficients they
/// represent (Section 7.8 of the Theora specification).
/// </summary>
/// <remarks>
/// Only the DC coefficient is predicted, and it is predicted in the quantised domain — before
/// dequantisation, from the quantised DC values of neighbours already recovered. That is why every
/// block of a frame shares one quantisation index for its DC coefficient even in Theora, where the AC
/// coefficients may each use a different one: a predictor built from values quantised differently
/// from the value it predicts would not predict anything.
/// <para/>
/// The predictor is a weighted sum of up to four neighbours — left, below-left, below and
/// below-right — and only of those that are coded and whose macro block predicts from the same
/// reference frame. Same reference frame is judged by the coding mode alone: an intra block, a block
/// from the previous frame and a block from the golden frame are three different things here even on
/// the first inter frame after an intra frame, where the previous and golden frames hold the same
/// picture. The weights are not an average; where the left, below-left and below neighbours are all
/// available the below-left one has a weight of minus twenty-six, which extrapolates a gradient
/// across the block rather than interpolating within it.
/// <para/>
/// Because the extrapolating cases can run away from every value they were built from, a predictor
/// that ends up more than 128 away from one of its three neighbours is thrown out and that
/// neighbour's value used instead. With no usable neighbour at all the predictor is the last DC value
/// seen for the same reference frame, which is zero at the start of each plane.
/// <para/>
/// The running total is held to sixteen bits by wrapping rather than clamping. Each block can add as
/// much as 580 to the predictor and the result becomes the next block's predictor, so a long enough
/// row of blocks can carry it past what sixteen bits hold; the encoder wrapped and so does this.
/// </remarks>
internal static class Vp3DcPrediction {

  /// <summary>How far a predictor may sit from a neighbour before that neighbour replaces it.</summary>
  private const int _OUTRANGE = 128;

  internal static void Undo(Vp3Geometry geometry, bool[] coded, byte[] modes, short[] coefficients) {
    var available = new bool[4];
    var neighbour = new int[4];
    var last = new short[3];

    for (var plane = 0; plane < 3; ++plane) {
      last[0] = last[1] = last[2] = 0;

      var width = geometry.PlaneBlockWidth[plane];
      var height = geometry.PlaneBlockHeight[plane];
      var index = geometry.CodedIndex[plane];

      for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var block = index[row * width + column];
        if (!coded[block])
          continue;

        var reference = Vp3Tables.ReferenceOfMode[modes[geometry.MacroblockOfBlock[block]]];

        available[0] = _Neighbour(geometry, coded, modes, index, width, column - 1, row, height, reference, out neighbour[0]);
        available[1] = _Neighbour(geometry, coded, modes, index, width, column - 1, row - 1, height, reference, out neighbour[1]);
        available[2] = _Neighbour(geometry, coded, modes, index, width, column, row - 1, height, reference, out neighbour[2]);
        available[3] = _Neighbour(geometry, coded, modes, index, width, column + 1, row - 1, height, reference, out neighbour[3]);

        var pattern = (available[0] ? 1 : 0) | (available[1] ? 2 : 0)
          | (available[2] ? 4 : 0) | (available[3] ? 8 : 0);

        var predictor = _Predict(available, neighbour, coefficients, last[reference]);
        var value = (short)(coefficients[block * 64] + predictor);
        coefficients[block * 64] = value;
        last[reference] = value;
      }
    }
  }

  /// <summary>
  /// Replaces the absolute quantised DC coefficient of every coded block with the residual VP3 stores
  /// in the packet, which is <see cref="Undo"/> run backwards.
  /// </summary>
  /// <remarks>
  /// The predictor a block sees is built from its neighbours' <em>absolute</em> values, because that
  /// is what the decoder will have recovered by the time it reaches the block. So the absolutes are
  /// taken aside first and the residuals written over them in the same order the decoder walks, which
  /// is per plane, row by row, coded blocks only.
  /// <para/>
  /// A neighbour counts only when it is coded <em>and</em> predicted from the same reference: the
  /// weighted sum is over blocks that mean the same thing, and a block predicted from the previous
  /// frame says nothing about one predicted from nothing. <see cref="ApplyIntra"/> is the same walk
  /// with that question answered in advance, every block being coded and intra.
  /// </remarks>
  internal static void Apply(Vp3Geometry geometry, bool[] coded, byte[] modes, short[] coefficients) {
    var absolute = new short[geometry.BlockCount];
    for (var block = 0; block < geometry.BlockCount; ++block)
      absolute[block] = coefficients[block * 64];

    var available = new bool[4];
    var neighbour = new int[4];
    var last = new short[3];

    for (var plane = 0; plane < 3; ++plane) {
      last[0] = last[1] = last[2] = 0;

      var width = geometry.PlaneBlockWidth[plane];
      var height = geometry.PlaneBlockHeight[plane];
      var index = geometry.CodedIndex[plane];

      for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var block = index[row * width + column];
        if (!coded[block])
          continue;

        var reference = Vp3Tables.ReferenceOfMode[modes[geometry.MacroblockOfBlock[block]]];

        available[0] = _Neighbour(geometry, coded, modes, index, width, column - 1, row, height, reference, out neighbour[0]);
        available[1] = _Neighbour(geometry, coded, modes, index, width, column - 1, row - 1, height, reference, out neighbour[1]);
        available[2] = _Neighbour(geometry, coded, modes, index, width, column, row - 1, height, reference, out neighbour[2]);
        available[3] = _Neighbour(geometry, coded, modes, index, width, column + 1, row - 1, height, reference, out neighbour[3]);

        var predictor = _PredictAbsolute(available, neighbour, absolute, last[reference]);
        coefficients[block * 64] = (short)(absolute[block] - predictor);
        last[reference] = absolute[block];
      }
    }
  }

  /// <summary>
  /// Replaces the absolute quantised DC coefficient of every block of an intra frame with the
  /// residual VP3 stores in the packet.
  /// </summary>
  internal static void ApplyIntra(Vp3Geometry geometry, short[] coefficients) {
    var absolute = new short[geometry.BlockCount];
    for (var block = 0; block < geometry.BlockCount; ++block)
      absolute[block] = coefficients[block * 64];

    var available = new bool[4];
    var neighbour = new int[4];

    for (var plane = 0; plane < 3; ++plane) {
      short last = 0;
      var width = geometry.PlaneBlockWidth[plane];
      var height = geometry.PlaneBlockHeight[plane];
      var index = geometry.CodedIndex[plane];

      for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var block = index[row * width + column];

        available[0] = _InBounds(width, height, column - 1, row, out var left);
        available[1] = _InBounds(width, height, column - 1, row - 1, out var belowLeft);
        available[2] = _InBounds(width, height, column, row - 1, out var below);
        available[3] = _InBounds(width, height, column + 1, row - 1, out var belowRight);
        neighbour[0] = available[0] ? index[left] : 0;
        neighbour[1] = available[1] ? index[belowLeft] : 0;
        neighbour[2] = available[2] ? index[below] : 0;
        neighbour[3] = available[3] ? index[belowRight] : 0;

        var predictor = _PredictAbsolute(available, neighbour, absolute, last);
        coefficients[block * 64] = (short)(absolute[block] - predictor);
        last = absolute[block];
      }
    }
  }

  private static int _Predict(bool[] available, int[] neighbour, short[] coefficients, short last) {
    var pattern = (available[0] ? 1 : 0) | (available[1] ? 2 : 0)
      | (available[2] ? 4 : 0) | (available[3] ? 8 : 0);
    if (pattern == 0)
      return last;

    var weights = Vp3Tables.DcPredictorWeights[pattern];
    var sum = 0;
    for (var i = 0; i < 4; ++i)
      if (available[i])
        sum += weights[i] * coefficients[neighbour[i] * 64];

    var predictor = sum / weights[4];
    if (!available[0] || !available[1] || !available[2])
      return predictor;

    var below = coefficients[neighbour[2] * 64];
    var left = coefficients[neighbour[0] * 64];
    var belowLeft = coefficients[neighbour[1] * 64];
    if (_Distance(predictor, below) > _OUTRANGE)
      return below;
    if (_Distance(predictor, left) > _OUTRANGE)
      return left;
    return _Distance(predictor, belowLeft) > _OUTRANGE ? belowLeft : predictor;
  }

  private static int _PredictAbsolute(bool[] available, int[] neighbour, short[] absolute, short last) {
    var pattern = (available[0] ? 1 : 0) | (available[1] ? 2 : 0)
      | (available[2] ? 4 : 0) | (available[3] ? 8 : 0);
    if (pattern == 0)
      return last;

    var weights = Vp3Tables.DcPredictorWeights[pattern];
    var sum = 0;
    for (var i = 0; i < 4; ++i)
      if (available[i])
        sum += weights[i] * absolute[neighbour[i]];

    var predictor = sum / weights[4];
    if (!available[0] || !available[1] || !available[2])
      return predictor;

    var below = absolute[neighbour[2]];
    var left = absolute[neighbour[0]];
    var belowLeft = absolute[neighbour[1]];
    if (_Distance(predictor, below) > _OUTRANGE)
      return below;
    if (_Distance(predictor, left) > _OUTRANGE)
      return left;
    return _Distance(predictor, belowLeft) > _OUTRANGE ? belowLeft : predictor;
  }

  /// <summary>
  /// Whether the block at a raster position can predict the current one, and which block it is.
  /// </summary>
  /// <remarks>
  /// It can when it exists, is coded, and its macro block predicts from the same reference frame.
  /// Positions off the left, right or bottom edge of the plane do not exist; there is no test against
  /// the top edge, because every neighbour consulted is on the current row or the one below it.
  /// </remarks>
  private static bool _Neighbour(
    Vp3Geometry geometry, bool[] coded, byte[] modes, int[] index, int width,
    int column, int row, int height, int reference, out int block) {
    block = 0;
    if (column < 0 || column >= width || row < 0 || row >= height)
      return false;

    block = index[row * width + column];
    return coded[block] && Vp3Tables.ReferenceOfMode[modes[geometry.MacroblockOfBlock[block]]] == reference;
  }

  private static bool _InBounds(int width, int height, int column, int row, out int rasterIndex) {
    if (column < 0 || column >= width || row < 0 || row >= height) {
      rasterIndex = 0;
      return false;
    }

    rasterIndex = row * width + column;
    return true;
  }

  private static int _Distance(int predictor, int value) {
    var difference = predictor - value;
    return difference < 0 ? -difference : difference;
  }
}
