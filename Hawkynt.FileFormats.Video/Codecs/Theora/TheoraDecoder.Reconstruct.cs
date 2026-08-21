using System;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Undoing DC prediction, and turning coefficients and predictors into samples.
/// </summary>
internal sealed partial class TheoraDecoder {

  /// <summary>
  /// Recovers each block's DC coefficient from the residual the stream carries — section 7.8.
  /// </summary>
  /// <remarks>
  /// The DC coefficient of a block is coded as a difference from a prediction made out of its
  /// already-decoded neighbours: the one to its left, the one below it, and the two diagonally below
  /// on each side. Which of the four count is not just a matter of being inside the frame — a
  /// neighbour has to be coded, and it has to predict from the *same reference frame*, judged by its
  /// macro block's coding mode alone. A block predicting from the golden frame and one predicting
  /// from the previous frame are on different scales even when the two frames hold the same picture,
  /// so mixing them would predict a value from the wrong one.
  /// <para/>
  /// This runs in raster order and not coded order, because the neighbours it needs are the ones
  /// before it in raster order. It also runs plane by plane, with the running last-value reset at
  /// each plane's start.
  /// <para/>
  /// The prediction is a weighted sum over whichever neighbours qualify, with weights from
  /// Table 7.47 chosen by which combination is available. The three-neighbour case extrapolates a
  /// gradient rather than averaging — the weights are 29, −26 and 29 over 32 — and is then checked
  /// for having run away: if the result is more than 128 from any of the three it is replaced by
  /// that neighbour's own value. Without that check a gradient across a busy area diverges.
  /// </remarks>
  private void _UndoDcPrediction(TheoraGeometry geometry) {
    Span<int> lastValues = stackalloc int[3];
    Span<int> predictorBlocks = stackalloc int[4];

    for (var plane = 0; plane < 3; ++plane) {
      lastValues.Clear();

      var blocksWide = geometry.PlaneBlocksWide[plane];
      var blocksHigh = geometry.PlaneBlocksHigh[plane];

      for (var row = 0; row < blocksHigh; ++row)
      for (var column = 0; column < blocksWide; ++column) {
        var block = geometry.BlockAt(plane, column, row);
        if (!this._coded[block])
          continue;

        var reference = TheoraTables.ReferenceFrameOf[this._modes[geometry.BlockMacroBlock[block]]];

        // Left, lower-left, lower, lower-right — the order Table 7.47 is indexed in.
        var available = 0;
        predictorBlocks.Clear();

        if (column > 0)
          this._Consider(geometry, plane, reference, 0, column - 1, row, ref available, predictorBlocks);
        if (column > 0 && row > 0)
          this._Consider(geometry, plane, reference, 1, column - 1, row - 1, ref available, predictorBlocks);
        if (row > 0)
          this._Consider(geometry, plane, reference, 2, column, row - 1, ref available, predictorBlocks);
        if (column < blocksWide - 1 && row > 0)
          this._Consider(geometry, plane, reference, 3, column + 1, row - 1, ref available, predictorBlocks);

        int prediction;
        if (available == 0)
          // Nothing beside it qualifies, so the most recent value from any block predicting from the
          // same reference frame stands in. At the start of a plane that is zero.
          prediction = lastValues[reference];
        else {
          var weights = TheoraTables.DcPredictorWeights[available];
          prediction = 0;
          for (var neighbour = 0; neighbour < 4; ++neighbour)
            if ((available & (1 << neighbour)) != 0)
              prediction += weights[neighbour] * this._coefficients[predictorBlocks[neighbour] * 64];

          prediction /= weights[4];

          // The runaway check, in the specification's order: the block below first, then the one to
          // the left, then the one below-left.
          if ((available & 0b0111) == 0b0111) {
            var below = this._coefficients[predictorBlocks[2] * 64];
            var left = this._coefficients[predictorBlocks[0] * 64];
            var belowLeft = this._coefficients[predictorBlocks[1] * 64];

            if (Math.Abs(prediction - below) > 128)
              prediction = below;
            else if (Math.Abs(prediction - left) > 128)
              prediction = left;
            else if (Math.Abs(prediction - belowLeft) > 128)
              prediction = belowLeft;
          }
        }

        // Truncated to sixteen bits rather than clamped. A token may add as much as 580 to a
        // prediction that then feeds the next block's, so the value can overflow — and the
        // specification says to throw the high bits away, not to saturate.
        var value = (short)(this._coefficients[block * 64] + prediction);
        this._coefficients[block * 64] = value;
        lastValues[reference] = value;
      }
    }
  }

  /// <summary>
  /// Notes a neighbour as a DC predictor if it is coded and predicts from the same reference frame.
  /// </summary>
  private void _Consider(
    TheoraGeometry geometry, int plane, int reference, int slot, int column, int row,
    ref int available, Span<int> predictorBlocks) {
    var neighbour = geometry.BlockAt(plane, column, row);
    if (!this._coded[neighbour])
      return;

    if (TheoraTables.ReferenceFrameOf[this._modes[geometry.BlockMacroBlock[neighbour]]] != reference)
      return;

    available |= 1 << slot;
    predictorBlocks[slot] = neighbour;
  }

  /// <summary>
  /// Builds every block of the frame from its predictor and its residual — section 7.9.4.
  /// </summary>
  /// <remarks>
  /// A coded block gets a predictor from its coding mode — a flat 128 for an intra block, or samples
  /// copied from a reference frame at its motion vector — and a residual from the inverse transform
  /// of its dequantised coefficients. An uncoded block is the co-located block of the previous
  /// frame, with no residual at all.
  /// <para/>
  /// The shortcut for a block whose only coefficient is the DC one is not an optimisation: it gives
  /// a different answer from running the full transform, because it skips the intermediate
  /// truncations, and the specification requires it to be used. Whether it applies is decided by the
  /// coefficient count the token layer kept, not by looking to see whether the other coefficients
  /// happen to be zero.
  /// </remarks>
  private void _Reconstruct(TheoraGeometry geometry) {
    var quantisation = this._quantisation!;
    var firstIndex = this._frameQuantisationIndices[0];

    Span<int> dequantised = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    Span<byte> predictor = stackalloc byte[64];

    for (var block = 0; block < geometry.BlockCount; ++block) {
      var plane = geometry.BlockPlane[block];
      var target = this._current!.Planes[plane];
      var width = this._current.Widths[plane];
      var x = geometry.BlockColumn[block] * TheoraGeometry.BLOCK_SIZE;
      var y = geometry.BlockRow[block] * TheoraGeometry.BLOCK_SIZE;

      if (!this._coded[block]) {
        // The co-located block of the previous frame, which is the whole-pixel predictor with a zero
        // vector, and no residual.
        this._Predict(this._previous!, plane, predictor, x, y, 0, 0);
        for (var row = 0; row < 8; ++row)
        for (var column = 0; column < 8; ++column)
          target[(y + row) * width + x + column] = predictor[row * 8 + column];

        continue;
      }

      var mode = (TheoraCodingMode)this._modes[geometry.BlockMacroBlock[block]];
      var quantisationType = mode == TheoraCodingMode.Intra ? 0 : 1;
      var reference = TheoraTables.ReferenceFrameOf[(int)mode];

      if (reference == 0)
        // The intra predictor is the constant 128, which exists only to centre an intra block's DC
        // range on zero.
        predictor.Fill(128);
      else
        this._PredictMotion(geometry, block, plane, predictor, x, y, reference == 2 ? this._golden! : this._previous!);

      if (this._coefficientCounts[block] < 2) {
        var matrix = quantisation.Matrix(quantisationType, plane, firstIndex);

        // Rounded and shifted in one step rather than dequantised and transformed. The full
        // transform of a DC-only block would truncate twice on the way and give a different answer.
        var flat = (short)((this._coefficients[block * 64] * matrix[0] + 15) >> 5);
        residual.Fill(flat);
      } else {
        var index = this._frameQuantisationIndices[this._quantisationIndices[block]];
        this._Dequantise(quantisation, block, plane, quantisationType, firstIndex, index, dequantised);
        TheoraInverseDct.Transform(dequantised, residual);
      }

      for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column) {
        var value = predictor[row * 8 + column] + residual[row * 8 + column];
        target[(y + row) * width + x + column] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  /// <summary>
  /// Dequantises a block's coefficients into natural order — section 7.9.2.
  /// </summary>
  /// <remarks>
  /// Two matrices, not one. The DC coefficient uses the frame's first quantisation index because DC
  /// prediction happens in the quantised domain; the AC coefficients use whichever of the frame's
  /// indices the block selected. The coefficients arrive in zig-zag order and leave in natural
  /// order, so the un-zig-zag happens here.
  /// </remarks>
  private void _Dequantise(
    TheoraQuantisation quantisation, int block, int plane, int quantisationType,
    int dcIndex, int acIndex, Span<int> dequantised) {
    var at = block * 64;

    var dcMatrix = quantisation.Matrix(quantisationType, plane, dcIndex);
    dequantised[0] = (short)(this._coefficients[at] * dcMatrix[0]);

    var acMatrix = quantisation.Matrix(quantisationType, plane, acIndex);
    for (var coefficient = 1; coefficient < 64; ++coefficient)
      // Truncated to sixteen bits rather than clamped: a large coefficient at a coarse quantiser can
      // dequantise to more than sixteen bits, and the specification throws the high ones away.
      dequantised[coefficient] = (short)(this._coefficients[at + TheoraTables.ZigZag[coefficient]] * acMatrix[coefficient]);
  }

  /// <summary>
  /// Builds a motion-compensated predictor for one block — sections 7.9.1.2 and 7.9.1.3.
  /// </summary>
  /// <remarks>
  /// A vector component is stored as an integer at half-pixel resolution in the luma plane. In a
  /// chroma plane it means the same displacement of the picture, so along an axis the chroma plane
  /// subsamples it works out at quarter-pixel resolution — which is why the divisor below depends on
  /// the pixel format and the axis, and why 4:2:2 divides its two axes differently.
  /// <para/>
  /// A fractional vector is turned into two whole-pixel ones, by truncating towards zero and away
  /// from it, and the two predictors averaged. Only two samples contribute even when both components
  /// are fractional — this is not a bilinear filter — and a quarter-pixel vector in a chroma plane
  /// is treated exactly like a half-pixel one.
  /// </remarks>
  private void _PredictMotion(
    TheoraGeometry geometry, int block, int plane, Span<byte> predictor, int x, int y, TheoraFrame source) {
    var (nearX, farX) = _Whole(this._motionX[block], this._motionDivisorX[plane]);
    var (nearY, farY) = _Whole(this._motionY[block], this._motionDivisorY[plane]);

    if (nearX == farX && nearY == farY) {
      this._Predict(source, plane, predictor, x, y, nearX, nearY);
      return;
    }

    var samples = source.Planes[plane];
    var width = source.Widths[plane];
    var height = source.Heights[plane];

    for (var row = 0; row < 8; ++row) {
      var nearRow = _Clamp(y + nearY + row, height);
      var farRow = _Clamp(y + farY + row, height);

      for (var column = 0; column < 8; ++column) {
        var nearColumn = _Clamp(x + nearX + column, width);
        var farColumn = _Clamp(x + farX + column, width);
        predictor[row * 8 + column] =
          (byte)((samples[nearRow * width + nearColumn] + samples[farRow * width + farColumn]) >> 1);
      }
    }
  }

  /// <summary>Copies an 8x8 predictor out of a reference plane at a whole-pixel offset — section 7.9.1.2.</summary>
  /// <remarks>
  /// A vector pointing outside the reference frame takes the nearest sample on its edge, which is
  /// what a decoder that pads its reference frames gets for free and what one that clamps its
  /// coordinates — as this one does — has to do explicitly.
  /// </remarks>
  private void _Predict(TheoraFrame source, int plane, Span<byte> predictor, int x, int y, int offsetX, int offsetY) {
    var samples = source.Planes[plane];
    var width = source.Widths[plane];
    var height = source.Heights[plane];

    for (var row = 0; row < 8; ++row) {
      var sourceRow = _Clamp(y + offsetY + row, height);
      for (var column = 0; column < 8; ++column)
        predictor[row * 8 + column] = samples[sourceRow * width + _Clamp(x + offsetX + column, width)];
    }
  }

  /// <summary>
  /// The two whole-pixel displacements a fractional motion vector component stands between.
  /// </summary>
  /// <remarks>
  /// Truncated towards zero and away from zero, both keeping the component's sign. Where the
  /// component divides exactly the two are equal and the whole-pixel predictor is used.
  /// </remarks>
  private static (int Near, int Far) _Whole(int component, int divisor) {
    var magnitude = component < 0 ? -component : component;
    var near = magnitude / divisor;
    var far = (magnitude + divisor - 1) / divisor;
    return component < 0 ? (-near, -far) : (near, far);
  }

  private static int _Clamp(int value, int limit) => value < 0 ? 0 : value >= limit ? limit - 1 : value;
}
