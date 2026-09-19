using System;

namespace FileFormat.Codecs.Vp5;

/// <summary>
/// VP5's inverse transform: the VP3 8x8 integer inverse DCT, in the two forms the codec uses it in.
/// </summary>
/// <remarks>
/// <b>Why this is not <see cref="Vp3.Vp3InverseDct"/>.</b> The transform is the same one, but the two
/// are not the same arithmetic. The VP3 file here is the Theora specification's normative form, which
/// truncates a sum to sixteen bits before each multiplication by the C4 constant; this one does not,
/// because VP5 has no specification and the implementation every VP5 file in the world was checked
/// against is FFmpeg's, which does not. The two differ only where an intermediate overflows sixteen
/// bits — rare, and not rare enough to guess at when the picture feeds the next picture's prediction.
/// <para/>
/// Two forms, and they are genuinely different results rather than the same result computed twice.
/// A block whose columns carry only a direct-current term takes the short path, which multiplies once
/// and rounds once instead of running the butterflies; a block that does not takes the long one. The
/// choice is made per column, on whether the row pass left anything below the top row, so one block
/// can take both. Using the long path for a direct-current column would be wrong and not merely slow.
/// <para/>
/// The intra form adds 128 to every output, which is where VP5's mid-grey comes from: an intra block's
/// direct-current coefficient is signed around grey rather than absolute. The inter form adds its
/// output to a prediction instead.
/// </remarks>
internal static class Vp5InverseDct {

  private const int _C1 = 64277;
  private const int _C2 = 60547;
  private const int _C3 = 54491;
  private const int _C4 = 46341;
  private const int _C5 = 36410;
  private const int _C6 = 25080;
  private const int _C7 = 12785;

  /// <summary>Rounding added before the column pass's division by sixteen.</summary>
  private const int _ROUNDING = 8;

  /// <summary>Writes the transform of an intra block over an 8x8 destination, mid-grey centred.</summary>
  internal static void Put(Span<short> coefficients, Span<byte> destination) {
    _FirstPass(coefficients);
    _SecondPass(coefficients, destination, add: false);
  }

  /// <summary>Adds the transform of an inter block's residual to an 8x8 prediction in place.</summary>
  internal static void Add(Span<short> coefficients, Span<byte> destination) {
    _FirstPass(coefficients);
    _SecondPass(coefficients, destination, add: true);
  }

  /// <summary>
  /// One constant times one value, keeping the low thirty-two bits and then the high sixteen.
  /// </summary>
  /// <remarks>
  /// The multiplication is written unsigned because it overflows: the second pass multiplies C4 by
  /// the sum of two sixteen-bit values, which reaches past what a signed int holds. Wrapping is the
  /// defined behaviour here and not an accident — the reference does the same thing, and an
  /// implementation that saturated or threw instead would disagree with it on exactly the blocks
  /// where it matters.
  /// </remarks>
  private static int _M(int constant, int value) => unchecked((int)((uint)constant * (uint)value)) >> 16;

  /// <summary>
  /// The first of the two one-dimensional passes, over the eight values eight apart from each other.
  /// </summary>
  /// <remarks>
  /// Neither pass is over "rows" in any sense the picture would recognise. The coefficients arrive
  /// transposed, because the scan order they are written in is transposed, so what this walks is a
  /// stride of eight and what <see cref="_SecondPass"/> walks is contiguous. Each output is truncated
  /// to sixteen bits, in the wrap-around sense.
  /// </remarks>
  private static void _FirstPass(Span<short> block) {
    for (var column = 0; column < 8; ++column) {
      if ((block[column] | block[column + 8] | block[column + 16] | block[column + 24]
           | block[column + 32] | block[column + 40] | block[column + 48] | block[column + 56]) == 0)
        continue;

      var a = _M(_C1, block[column + 8]) + _M(_C7, block[column + 56]);
      var b = _M(_C7, block[column + 8]) - _M(_C1, block[column + 56]);
      var c = _M(_C3, block[column + 24]) + _M(_C5, block[column + 40]);
      var d = _M(_C3, block[column + 40]) - _M(_C5, block[column + 24]);

      var ad = _M(_C4, a - c);
      var bd = _M(_C4, b - d);
      var cd = a + c;
      var dd = b + d;

      var e = _M(_C4, block[column] + block[column + 32]);
      var f = _M(_C4, block[column] - block[column + 32]);
      var g = _M(_C2, block[column + 16]) + _M(_C6, block[column + 48]);
      var h = _M(_C6, block[column + 16]) - _M(_C2, block[column + 48]);

      var ed = e - g;
      var gd = e + g;
      var add = f + ad;
      var bdd = bd - h;
      var fd = f - ad;
      var hd = bd + h;

      block[column] = unchecked((short)(gd + cd));
      block[column + 8] = unchecked((short)(add + hd));
      block[column + 16] = unchecked((short)(add - hd));
      block[column + 24] = unchecked((short)(ed + dd));
      block[column + 32] = unchecked((short)(ed - dd));
      block[column + 40] = unchecked((short)(fd + bdd));
      block[column + 48] = unchecked((short)(fd - bdd));
      block[column + 56] = unchecked((short)(gd - cd));
    }
  }

  /// <summary>
  /// The second one-dimensional pass, over eight contiguous values, writing one column of the block.
  /// </summary>
  /// <remarks>
  /// This is where the rounding and the division by sixteen live, and where the two forms part
  /// company: the intra form folds 128 into the two terms every output carries, so the whole block
  /// comes out centred on grey, and the inter form adds what it computes to what the prediction
  /// already holds.
  /// </remarks>
  private static void _SecondPass(Span<short> block, Span<byte> destination, bool add) {
    for (var column = 0; column < 8; ++column) {
      var at = column * 8;

      if ((block[at + 1] | block[at + 2] | block[at + 3] | block[at + 4]
           | block[at + 5] | block[at + 6] | block[at + 7]) == 0) {
        _DirectCurrentColumn(block[at], destination, column, add);
        continue;
      }

      var a = _M(_C1, block[at + 1]) + _M(_C7, block[at + 7]);
      var b = _M(_C7, block[at + 1]) - _M(_C1, block[at + 7]);
      var c = _M(_C3, block[at + 3]) + _M(_C5, block[at + 5]);
      var d = _M(_C3, block[at + 5]) - _M(_C5, block[at + 3]);

      var ad = _M(_C4, a - c);
      var bd = _M(_C4, b - d);
      var cd = a + c;
      var dd = b + d;

      var e = _M(_C4, block[at] + block[at + 4]) + _ROUNDING;
      var f = _M(_C4, block[at] - block[at + 4]) + _ROUNDING;
      if (!add) {
        e += 16 * 128;
        f += 16 * 128;
      }

      var g = _M(_C2, block[at + 2]) + _M(_C6, block[at + 6]);
      var h = _M(_C6, block[at + 2]) - _M(_C2, block[at + 6]);

      var ed = e - g;
      var gd = e + g;
      var add2 = f + ad;
      var bdd = bd - h;
      var fd = f - ad;
      var hd = bd + h;

      _Store(destination, column, 0, (gd + cd) >> 4, add);
      _Store(destination, column, 1, (add2 + hd) >> 4, add);
      _Store(destination, column, 2, (add2 - hd) >> 4, add);
      _Store(destination, column, 3, (ed + dd) >> 4, add);
      _Store(destination, column, 4, (ed - dd) >> 4, add);
      _Store(destination, column, 5, (fd + bdd) >> 4, add);
      _Store(destination, column, 6, (fd - bdd) >> 4, add);
      _Store(destination, column, 7, (gd - cd) >> 4, add);
    }
  }

  private static void _DirectCurrentColumn(short direct, Span<byte> destination, int column, bool add) {
    var value = (_C4 * direct + (_ROUNDING << 16)) >> 20;

    if (!add) {
      var grey = _Clamp(128 + value);
      for (var row = 0; row < 8; ++row)
        destination[row * 8 + column] = grey;
      return;
    }

    if (direct == 0)
      return;

    for (var row = 0; row < 8; ++row) {
      var at = row * 8 + column;
      destination[at] = _Clamp(destination[at] + value);
    }
  }

  private static void _Store(Span<byte> destination, int column, int row, int value, bool add) {
    var at = row * 8 + column;
    destination[at] = _Clamp(add ? destination[at] + value : value);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
