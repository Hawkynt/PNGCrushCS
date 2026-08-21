namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Forms the eight-by-eight predictor a block's residual is added to (Section 7.9.1).
/// </summary>
/// <remarks>
/// There are three kinds and the coding mode and motion vector pick between them. An intra block is
/// predicted by the constant 128, which does nothing but centre the range of DC values it can code
/// around zero. Every other block is predicted from a reference frame, whole-pixel when both
/// components of its motion vector land on a sample and half-pixel when either does not.
/// <para/>
/// Motion vectors are stated in half-pixel steps for the luma plane. In 4:2:0 both chroma axes are
/// subsampled, so the same number is a quarter of a chroma pixel — and a quarter-pixel offset is
/// treated exactly as a half-pixel one, averaging the two whole-pixel positions it lies between
/// rather than weighting them by how close it is to each.
/// <para/>
/// Only two samples ever contribute to a half-pixel predictor, even when both components are
/// fractional: the position with both components truncated towards zero and the position with both
/// truncated away from it. That is a diagonal average, not the four-sample average a separable
/// bilinear filter would give, and it is what makes VP3's half-pixel prediction cheap.
/// <para/>
/// A vector that points outside the reference frame reads the nearest sample on the edge instead.
/// Most decoders get that by growing the reference frame by a border of repeated edge samples; this
/// clamps the coordinates, which gives the same samples for any distance rather than for the finite
/// one a border covers.
/// </remarks>
internal static class Vp3Prediction {

  /// <summary>What an intra block is predicted by, which centres its DC range on zero.</summary>
  internal const int INTRA_PREDICTOR = 128;

  internal static void Intra(int[] predictor) {
    for (var i = 0; i < 64; ++i)
      predictor[i] = INTRA_PREDICTOR;
  }

  /// <summary>
  /// Predicts a block from a reference plane along a motion vector.
  /// </summary>
  /// <param name="reference">The reference plane, row zero at the bottom.</param>
  /// <param name="planeWidth">The plane's width in samples.</param>
  /// <param name="planeHeight">The plane's height in samples.</param>
  /// <param name="originX">The horizontal sample index of the block's lower-left corner.</param>
  /// <param name="originY">The vertical sample index of the block's lower-left corner.</param>
  /// <param name="motionX">The horizontal motion vector component, in <paramref name="steps"/>ths of a sample.</param>
  /// <param name="motionY">The vertical motion vector component, in the same units.</param>
  /// <param name="steps">How many motion vector steps make one sample of this plane: two for luma, four for chroma in 4:2:0.</param>
  /// <param name="predictor">Sixty-four predictor values to fill, row-major, row zero at the bottom.</param>
  internal static void Inter(
    byte[] reference, int planeWidth, int planeHeight, int originX, int originY,
    int motionX, int motionY, int steps, int[] predictor) {
    var (nearX, farX) = _Whole(motionX, steps);
    var (nearY, farY) = _Whole(motionY, steps);

    if (nearX == farX && nearY == farY) {
      for (var row = 0; row < 8; ++row) {
        var sourceRow = _Clamp(originY + nearY + row, planeHeight) * planeWidth;
        for (var column = 0; column < 8; ++column)
          predictor[row * 8 + column] = reference[sourceRow + _Clamp(originX + nearX + column, planeWidth)];
      }

      return;
    }

    for (var row = 0; row < 8; ++row) {
      var nearRow = _Clamp(originY + nearY + row, planeHeight) * planeWidth;
      var farRow = _Clamp(originY + farY + row, planeHeight) * planeWidth;
      for (var column = 0; column < 8; ++column) {
        var near = reference[nearRow + _Clamp(originX + nearX + column, planeWidth)];
        var far = reference[farRow + _Clamp(originX + farX + column, planeWidth)];
        predictor[row * 8 + column] = near + far >> 1;
      }
    }
  }

  /// <summary>
  /// The two whole-sample offsets a motion vector component lies between.
  /// </summary>
  /// <remarks>
  /// The first truncates towards zero and the second away from it, so a component that already lands
  /// on a sample gives the same offset twice — which is how the whole-pixel case is recognised.
  /// </remarks>
  private static (int Near, int Far) _Whole(int component, int steps) {
    var magnitude = component < 0 ? -component : component;
    var near = magnitude / steps;
    var far = (magnitude + steps - 1) / steps;
    return component < 0 ? (-near, -far) : (near, far);
  }

  private static int _Clamp(int value, int limit) => value < 0 ? 0 : value >= limit ? limit - 1 : value;
}
