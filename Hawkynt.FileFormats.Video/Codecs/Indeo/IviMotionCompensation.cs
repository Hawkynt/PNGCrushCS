namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Predicts a block from a reference band, at whole- or half-sample accuracy, either replacing the
/// block or adding to a residual already there.
/// </summary>
/// <remarks>
/// Prediction happens in the band's own coefficient domain and not on finished pixels: the band
/// buffers hold biased samples in sixteen bits, and a predicted block is either written over the
/// buffer (an uncoded block, which is the reference unchanged) or added to what the inverse transform
/// has already put there (a coded block, whose coefficients are a residual). That is why every
/// prediction here comes in both forms.
/// <para/>
/// Half-sample positions are averaged with a truncating shift and no rounding term, in one dimension
/// or in both; the two-dimensional case averages the four surrounding samples at once rather than
/// twice in sequence, which is not the same value.
/// </remarks>
internal static class IviMotionCompensation {

  /// <summary>Predicts one square block from one reference.</summary>
  /// <param name="type">
  /// Which half-sample position: 0 for a whole sample, 1 shifted half a sample right, 2 half a sample
  /// down, 3 both.
  /// </param>
  /// <param name="add">Whether to add to what is in the destination rather than replace it.</param>
  internal static void Predict(
    short[] destination, int destinationOffset, int destinationPitch,
    short[] reference, int referenceOffset, int referencePitch,
    int size, int type, bool add) {
    for (var row = 0; row < size; ++row) {
      var to = destinationOffset + row * destinationPitch;
      var from = referenceOffset + row * referencePitch;
      var below = from + referencePitch;

      for (var column = 0; column < size; ++column) {
        var predicted = type switch {
          1 => (reference[from + column] + reference[from + column + 1]) >> 1,
          2 => (reference[from + column] + reference[below + column]) >> 1,
          3 => (reference[from + column] + reference[from + column + 1]
                + reference[below + column] + reference[below + column + 1]) >> 2,
          _ => reference[from + column],
        };

        destination[to + column] = (short)(add ? destination[to + column] + predicted : predicted);
      }
    }
  }

  /// <summary>
  /// Predicts one square block from two references at once, which is what a bidirectional macroblock
  /// asks for.
  /// </summary>
  /// <remarks>
  /// The two predictions are summed in a sixteen-bit scratch block and then halved, so the sum wraps
  /// before the halving rather than after it. Averaging the two predictions in wider arithmetic would
  /// differ wherever the sum passes 32767, which real streams reach.
  /// </remarks>
  internal static void PredictAverage(
    short[] destination, int destinationOffset, int pitch,
    short[] first, int firstOffset, short[] second, int secondOffset,
    int size, int firstType, int secondType, bool add) {
    var scratch = new short[size * size];

    Predict(scratch, 0, size, first, firstOffset, pitch, size, firstType, add: false);
    Predict(scratch, 0, size, second, secondOffset, pitch, size, secondType, add: true);

    for (var row = 0; row < size; ++row) {
      var to = destinationOffset + row * pitch;

      for (var column = 0; column < size; ++column) {
        var predicted = scratch[row * size + column] >> 1;
        destination[to + column] = (short)(add ? destination[to + column] + predicted : predicted);
      }
    }
  }
}
