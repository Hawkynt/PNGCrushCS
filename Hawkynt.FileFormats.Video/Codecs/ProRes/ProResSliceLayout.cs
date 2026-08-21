using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// How the macroblocks of one macroblock row are grouped into slices.
/// </summary>
/// <remarks>
/// RDD 36:2022, 6.2. The layout is the same for every macroblock row of a picture and follows
/// entirely from two numbers: the width of the picture in macroblocks and the desired slice size the
/// picture header states. Slices of the desired size are taken from the left until fewer than that
/// many macroblocks remain, then the size halves, and so on until the row is used up — so the sizes
/// along a row are non-increasing powers of two, and 45 macroblocks with a desired size of eight
/// come out as 8, 8, 8, 8, 8, 4, 1 (the specification's own worked example, 4).
/// <para/>
/// The halving is what makes a slice always a power of two macroblocks wide, which the slice
/// scanning of 7.2.1 relies on: the index of a coefficient within the scanned array is a frequency
/// index, a macroblock index and a block index packed side by side in one number, and that packing
/// only works because none of the three ever carries into the next.
/// </remarks>
internal static class ProResSliceLayout {

  /// <summary>
  /// The sizes in macroblocks of the slices of one macroblock row, left to right.
  /// </summary>
  /// <param name="widthInMacroblocks">The width of the encoded picture in macroblocks.</param>
  /// <param name="log2DesiredSliceSize">The picture header's <c>log2_desired_slice_size_in_mb</c>.</param>
  internal static int[] Build(int widthInMacroblocks, int log2DesiredSliceSize) {
    if (widthInMacroblocks <= 0)
      throw new InvalidDataException("A ProRes picture states a width of no macroblocks at all.");

    var sizes = new List<int>();
    var sliceSize = 1 << log2DesiredSliceSize;
    var remaining = widthInMacroblocks;

    do {
      while (remaining >= sliceSize) {
        sizes.Add(sliceSize);
        remaining -= sliceSize;
      }

      sliceSize /= 2;
    } while (remaining > 0);

    return sizes.ToArray();
  }
}
