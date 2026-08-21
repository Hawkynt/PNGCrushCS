using System;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// The 384 quantisation matrices a stream defines, built once from the setup header's parameters.
/// </summary>
/// <remarks>
/// Theora specification section 6.4.3. A matrix exists for every combination of quantisation type
/// (intra or not), colour plane and quantisation index — 2 × 3 × 64 of them — and the setup header
/// stores none of them directly. What it stores is a handful of base matrices and, for each type and
/// plane, a partition of the quantisation scale into ranges; a matrix for an index inside a range is
/// the linear interpolation between the base matrices at the range's two ends.
/// <para/>
/// The specification's own procedure recomputes a matrix for every block, and says in as many words
/// that an implementation should compute them once instead. All 384 are built here when the stream
/// is opened, which costs about 50 kB and removes the interpolation from the inner loop entirely.
/// <para/>
/// The scaling is worth stating because two factors are folded into it. The product of a scale value
/// and a base matrix entry is in hundredths of a sample value, so it is divided by 100; and the
/// transform this feeds is four times the size of the orthonormal one, so the result is multiplied
/// by four to match. The value is then clamped below by a per-type minimum from Table 6.18 and above
/// by 4096.
/// </remarks>
internal sealed class TheoraQuantisation {

  private const int _QUANTISATION_INDICES = 64;
  private const int _COEFFICIENTS = 64;
  private const int _QUANTISATION_TYPES = 2;
  private const int _PLANES = 3;

  /// <summary>The largest value a quantisation matrix entry may take — section 6.4.3.</summary>
  private const int _MAX_QUANTISER = 4096;

  /// <summary>
  /// The smallest a quantiser may be, by quantisation type and by whether the coefficient is the DC
  /// one — Table 6.18.
  /// </summary>
  /// <remarks>
  /// Indexed as <c>[qti * 2 + (ci == 0 ? 0 : 1)]</c>: 16 and 8 for an intra block's DC and AC
  /// coefficients, 32 and 16 for any other block's. Inter blocks are quantised at least twice as
  /// coarsely as intra ones at the same index, which is what keeps a long run of predicted frames
  /// from accumulating detail the reference frame does not have.
  /// </remarks>
  private static readonly int[] _minimumQuantiser = [16, 8, 32, 16];

  /// <summary>
  /// Every matrix, flattened: <c>[((qti * 3 + pli) * 64 + qi) * 64 + ci]</c>, in natural order.
  /// </summary>
  private readonly ushort[] _matrices = new ushort[_QUANTISATION_TYPES * _PLANES * _QUANTISATION_INDICES * _COEFFICIENTS];

  internal TheoraQuantisation(TheoraSetupHeader setup) {
    var interpolated = new int[_COEFFICIENTS];

    for (var type = 0; type < _QUANTISATION_TYPES; ++type)
    for (var plane = 0; plane < _PLANES; ++plane) {
      var sizes = setup.RangeSizes[type, plane];
      var endpoints = setup.RangeMatrices[type, plane];
      var ranges = setup.RangeCounts[type, plane];

      for (var index = 0; index < _QUANTISATION_INDICES; ++index) {
        // The range this index falls in, and where that range begins and ends on the scale. An index
        // sitting exactly on a boundary belongs to either and gives the same matrix from both, so
        // the first one found is taken.
        var range = 0;
        var start = 0;
        while (range < ranges - 1 && start + sizes[range] < index)
          start += sizes[range++];

        var end = start + sizes[range];
        var width = sizes[range];
        var low = setup.BaseMatrices[endpoints[range]];
        var high = setup.BaseMatrices[endpoints[range + 1]];

        for (var coefficient = 0; coefficient < _COEFFICIENTS; ++coefficient)
          // Rounded rather than truncated: the numerator carries the range's own width as the
          // rounding term, which is what makes an index halfway between two base matrices give
          // their average rather than the lower of the two.
          interpolated[coefficient] =
            (2 * (end - index) * low[coefficient] + 2 * (index - start) * high[coefficient] + width) / (2 * width);

        var target = ((type * _PLANES + plane) * _QUANTISATION_INDICES + index) * _COEFFICIENTS;
        for (var coefficient = 0; coefficient < _COEFFICIENTS; ++coefficient) {
          var scale = coefficient == 0 ? setup.DcScale[index] : setup.AcScale[index];
          var minimum = _minimumQuantiser[type * 2 + (coefficient == 0 ? 0 : 1)];
          var value = scale * interpolated[coefficient] / 100 * 4;
          if (value > _MAX_QUANTISER)
            value = _MAX_QUANTISER;
          if (value < minimum)
            value = minimum;

          this._matrices[target + coefficient] = (ushort)value;
        }
      }
    }
  }

  /// <summary>One quantisation matrix, in natural coefficient order.</summary>
  internal ReadOnlySpan<ushort> Matrix(int quantisationType, int plane, int quantisationIndex)
    => this._matrices.AsSpan(
      ((quantisationType * _PLANES + plane) * _QUANTISATION_INDICES + quantisationIndex) * _COEFFICIENTS,
      _COEFFICIENTS);
}
