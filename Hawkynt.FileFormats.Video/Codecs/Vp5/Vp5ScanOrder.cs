namespace FileFormat.Codecs.Vp5;

/// <summary>
/// Where the coefficient at each scan position lands in the block the inverse transform reads.
/// </summary>
/// <remarks>
/// Two things at once, which is why it is one table and not two. The first is the ordinary zigzag:
/// coefficients are transmitted from the lowest frequency outwards, so scan position <c>n</c> names
/// row and column of its own. The second is a transposition — row and column exchanged — which is
/// there because VP5's transform runs its two one-dimensional passes in the opposite order from the
/// one the scan implies. Transposing once, here, is cheaper than transposing a block twice, and it is
/// what the reference implementation does, so the block layout this produces is the layout
/// <see cref="Vp5InverseDct"/> expects.
/// <para/>
/// Built rather than transcribed. The zigzag is the standard one and has a definition; a second
/// rendering of a table that can be generated is a second chance to get it wrong.
/// </remarks>
internal static class Vp5ScanOrder {

  /// <summary>The block position each of the sixty-four scan positions writes to.</summary>
  internal static readonly byte[] Positions = _Build();

  private static byte[] _Build() {
    var positions = new byte[64];
    var x = 0;
    var y = 0;

    for (var index = 0; index < 64; ++index) {
      // Transposed on the way in: the block is addressed column-major relative to the scan.
      positions[index] = (byte)(x * 8 + y);

      // The zigzag walks diagonals, alternating direction, and turns at the edges. Which edge it has
      // reached decides whether the step is down-and-left or up-and-right on the next diagonal.
      if (((x + y) & 1) == 0) {
        if (x == 7)
          ++y;
        else if (y == 0)
          ++x;
        else {
          ++x;
          --y;
        }
      } else {
        if (y == 7)
          ++x;
        else if (x == 0)
          ++y;
        else {
          --x;
          ++y;
        }
      }
    }

    return positions;
  }
}
