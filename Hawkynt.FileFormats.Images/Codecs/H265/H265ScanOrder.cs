using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The three orders coefficients are read in — ITU-T H.265, clauses 6.5.3 to 6.5.5.
/// </summary>
/// <remarks>
/// HEVC scans a transform block twice over: once across the four-by-four sub-blocks it is divided
/// into, and once within each sub-block, both in the same order. So the tables are generated for
/// every block size from four samples across down to one sub-block, and the residual coder indexes
/// them by <c>log2TrafoSize − 2</c> for the sub-block grid and by 2 for the coefficients inside one.
/// <para/>
/// The scan is chosen from the intra prediction mode for the smaller blocks, and that choice is the
/// point of having three: a block predicted from the left has its energy in the vertical direction,
/// and reading it in the order the energy runs puts the significant coefficients together where the
/// context model can find them. Everything else uses the diagonal.
/// <para/>
/// Generated rather than tabulated because the standard specifies them as algorithms rather than as
/// tables. The diagonal one in particular is a loop whose bounds are easy to write down and awkward
/// to write out: it walks each anti-diagonal from its bottom-left end, and the entries that fall
/// outside the block are simply not emitted.
/// </remarks>
internal static class H265ScanOrder {

  /// <summary>The diagonal scan — <c>scanIdx</c> 0.</summary>
  internal const int DIAGONAL = 0;

  /// <summary>The horizontal scan — <c>scanIdx</c> 1.</summary>
  internal const int HORIZONTAL = 1;

  /// <summary>The vertical scan — <c>scanIdx</c> 2.</summary>
  internal const int VERTICAL = 2;

  /// <summary>Block sizes 1, 2, 4, 8, 16 and 32 across, indexed by the base-two logarithm.</summary>
  private const int _LOG2_SIZES = 6;

  private static readonly byte[][][] _orders = _Build();

  /// <summary>
  /// The scan positions of a block <c>1 &lt;&lt; log2BlockSize</c> across, as interleaved x and y.
  /// </summary>
  /// <remarks>
  /// Interleaved into one array rather than kept as pairs because every caller reads both halves of
  /// an entry together and this is on the hottest path in the decoder: a coefficient block of
  /// thirty-two samples across is read up to a thousand and twenty-four times per transform unit.
  /// </remarks>
  internal static byte[] Positions(int log2BlockSize, int scanIdx) => _orders[log2BlockSize][scanIdx];

  /// <summary>The x of scan position <paramref name="index"/>.</summary>
  internal static int X(byte[] order, int index) => order[index << 1];

  /// <summary>The y of scan position <paramref name="index"/>.</summary>
  internal static int Y(byte[] order, int index) => order[(index << 1) + 1];

  private static byte[][][] _Build() {
    var orders = new byte[_LOG2_SIZES][][];

    for (var log2Size = 0; log2Size < _LOG2_SIZES; ++log2Size) {
      var size = 1 << log2Size;
      orders[log2Size] = [_Diagonal(size), _Horizontal(size), _Vertical(size)];
    }

    return orders;
  }

  /// <summary>The up-right diagonal scan of clause 6.5.3.</summary>
  private static byte[] _Diagonal(int size) {
    var scan = new byte[size * size * 2];
    var i = 0;
    var x = 0;
    var y = 0;

    while (true) {
      while (y >= 0) {
        if (x < size && y < size) {
          scan[i << 1] = (byte)x;
          scan[(i << 1) + 1] = (byte)y;
          ++i;
        }

        --y;
        ++x;
      }

      y = x;
      x = 0;

      if (i >= size * size)
        return scan;
    }
  }

  /// <summary>The horizontal scan of clause 6.5.4: whole rows, top to bottom.</summary>
  private static byte[] _Horizontal(int size) {
    var scan = new byte[size * size * 2];
    var i = 0;

    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        scan[i << 1] = (byte)x;
        scan[(i << 1) + 1] = (byte)y;
        ++i;
      }

    return scan;
  }

  /// <summary>The vertical scan of clause 6.5.5: whole columns, left to right.</summary>
  private static byte[] _Vertical(int size) {
    var scan = new byte[size * size * 2];
    var i = 0;

    for (var x = 0; x < size; ++x)
      for (var y = 0; y < size; ++y) {
        scan[i << 1] = (byte)x;
        scan[(i << 1) + 1] = (byte)y;
        ++i;
      }

    return scan;
  }
}
