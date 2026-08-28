using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The coefficients of one transform block (specification 6.4.24 to 6.4.26).
/// </summary>
/// <remarks>
/// Coefficients are read in a scan order that runs from the corner outwards, so the large ones come
/// first and the run of zeroes that natural pictures end with is at the end where a single flag can
/// dismiss it. That flag — "are there any more" — is read before each coefficient except immediately
/// after a zero, since two consecutive end-of-block tests would be one wasted bool.
/// <para/>
/// The probability of each coefficient depends on the two already-decoded coefficients nearest it in
/// the block, which are not the two before it in scan order: which two they are depends on the
/// transform, because a block transformed with the sine transform along its rows has its energy
/// arranged differently from one that used the cosine transform.
/// <para/>
/// Only three of the eleven probabilities of the token tree are carried in the bitstream. The rest
/// are looked up from the third in the Pareto table, on the reasoning that the tail of the
/// distribution has one shape and only its scale needs stating.
/// </remarks>
internal sealed partial class Vp9FrameDecoder {

  /// <summary>
  /// Reads one transform block's coefficients into <see cref="_tokens"/>.
  /// </summary>
  /// <returns>Whether the block ended anywhere but at its first position.</returns>
  private byte _ReadTokens(int plane, int startX, int startY, int transformSize, int blockIndex) {
    var scan = this._Scan(plane, transformSize, blockIndex);
    var endOfBlock = 16 << (transformSize << 1);
    var width = 4 << transformSize;

    var planeType = plane > 0 ? 1 : 0;
    var referenceType = this._isInter ? 1 : 0;
    var probabilities = this._probabilities.Coefficient;
    var bands = transformSize == TX_4X4 ? Vp9Tables.CoefficientBand4x4 : Vp9Tables.CoefficientBand8x8Plus;

    var checkEndOfBlock = true;
    var context = this._FirstCoefficientContext(plane, startX, startY, transformSize);
    var c = 0;

    for (; c < endOfBlock; ++c) {
      var position = scan[c];
      var band = bands[c];

      if (c > 0)
        context = this._NeighbourContext(position, width);

      var at = CoefficientContext(transformSize, planeType, referenceType, band, context);

      if (checkEndOfBlock) {
        var more = this._reader.ReadBool(probabilities[at * UNCONSTRAINED_NODES]);
        ++this._counts.MoreCoefficients[at * 2 + more];
        if (more == 0)
          break;
      }

      var token = this._ReadToken(probabilities, at);
      ++this._counts.Token[at * UNCONSTRAINED_NODES + Math.Min(2, token)];
      this._tokenCache[position] = Vp9Tables.EnergyClass[token];

      if (token == ZERO_TOKEN) {
        this._tokens[position] = 0;
        checkEndOfBlock = false;
        continue;
      }

      var coefficient = this._ReadCoefficient(token);
      this._tokens[position] = this._reader.ReadLiteral(1) != 0 ? -coefficient : coefficient;
      checkEndOfBlock = true;
    }

    var nonzero = c > 0;
    if (nonzero)
      ++this._eobTotal;

    for (var i = c; i < endOfBlock; ++i)
      this._tokens[scan[i]] = 0;

    return nonzero ? (byte)1 : (byte)0;
  }

  // ============================================================================================
  // Scan order (specification 6.4.25)
  // ============================================================================================

  /// <summary>
  /// Chooses the scan order, and with it the pair of one-dimensional transforms the block will use.
  /// </summary>
  /// <remarks>
  /// The transform type is deduced from the intra prediction mode rather than coded. A block
  /// predicted from the row above has its error growing downwards, and the sine transform — whose
  /// basis functions vanish at one end — describes that better than the cosine transform does. An
  /// inter block, a chrominance block and a 32x32 block all use the plain cosine transform in both
  /// directions.
  /// </remarks>
  private short[] _Scan(int plane, int transformSize, int blockIndex) {
    if (plane > 0 || transformSize == TX_32X32)
      this._transformType = DCT_DCT;
    else if (transformSize == TX_4X4)
      this._transformType = this._header.Lossless || this._isInter
        ? DCT_DCT
        : Vp9Tables.ModeToTransformType[this._miSize < BLOCK_8X8 ? this._subModes[blockIndex] : this._yMode];
    else
      this._transformType = Vp9Tables.ModeToTransformType[this._yMode];

    return transformSize switch {
      TX_4X4 => this._transformType switch {
        ADST_DCT => Vp9Tables.RowScan4x4,
        DCT_ADST => Vp9Tables.ColumnScan4x4,
        _ => Vp9Tables.DefaultScan4x4,
      },
      TX_8X8 => this._transformType switch {
        ADST_DCT => Vp9Tables.RowScan8x8,
        DCT_ADST => Vp9Tables.ColumnScan8x8,
        _ => Vp9Tables.DefaultScan8x8,
      },
      TX_16X16 => this._transformType switch {
        ADST_DCT => Vp9Tables.RowScan16x16,
        DCT_ADST => Vp9Tables.ColumnScan16x16,
        _ => Vp9Tables.DefaultScan16x16,
      },
      _ => Vp9Tables.DefaultScan32x32,
    };
  }

  // ============================================================================================
  // Contexts (specification 9.3.2)
  // ============================================================================================

  /// <summary>
  /// The context of the first coefficient, which comes from whether the neighbouring transform
  /// blocks held any coefficients at all.
  /// </summary>
  private int _FirstCoefficientContext(int plane, int startX, int startY, int transformSize) {
    var subX = plane > 0 ? this._header.SubsamplingX : 0;
    var subY = plane > 0 ? this._header.SubsamplingY : 0;
    var maxX = (2 * this._header.MiCols) >> subX;
    var maxY = (2 * this._header.MiRows) >> subY;
    var points = 1 << transformSize;

    var x4 = startX >> 2;
    var y4 = startY >> 2;
    var above = 0;
    var left = 0;

    for (var i = 0; i < points; ++i) {
      if (x4 + i < maxX)
        above |= this._aboveNonzero[plane][x4 + i];

      if (y4 + i < maxY)
        left |= this._leftNonzero[plane][y4 + i];
    }

    return above + left;
  }

  /// <summary>
  /// The context of every coefficient after the first, from the two already-decoded neighbours the
  /// transform type says are nearest.
  /// </summary>
  private int _NeighbourContext(int position, int width) {
    var row = position / width;
    var column = position % width;

    int first;
    int second;

    if (row > 0 && column > 0) {
      var fromAbove = (row - 1) * width + column;
      var fromLeft = row * width + column - 1;

      switch (this._transformType) {
        case DCT_ADST:
          first = second = fromAbove;
          break;
        case ADST_DCT:
          first = second = fromLeft;
          break;
        default:
          first = fromAbove;
          second = fromLeft;
          break;
      }
    } else if (row > 0)
      first = second = (row - 1) * width + column;
    else
      first = second = row * width + column - 1;

    return (1 + this._tokenCache[first] + this._tokenCache[second]) >> 1;
  }

  // ============================================================================================
  // The token and its extra bits (specification 6.4.24 and 6.4.26)
  // ============================================================================================

  private int _ReadToken(byte[] probabilities, int at) {
    var tree = Vp9Trees.Token;
    var node = 0;

    do {
      var level = node >> 1;
      var probability = _Pareto(level, probabilities[at * UNCONSTRAINED_NODES + Math.Min(2, 1 + level)]);
      node = tree[node + this._reader.ReadBool(probability)];
    } while (node > 0);

    return -node;
  }

  /// <summary>
  /// The probability of a token tree node below the third, derived from the third
  /// (specification 9.3.2).
  /// </summary>
  private static int _Pareto(int node, int probability) {
    if (node < 2)
      return probability;

    var row = (probability - 1) / 2;
    var column = node - 2;

    // An odd probability names a row of the table exactly; an even one falls between two rows and is
    // read as their average, which is what makes 255 probabilities fit a table of 128 rows.
    return (probability & 1) != 0
      ? Vp9Tables.ParetoTable[row * 8 + column]
      : (Vp9Tables.ParetoTable[row * 8 + column] + Vp9Tables.ParetoTable[(row + 1) * 8 + column]) >> 1;
  }

  private const int _CATEGORY_SIX = 6;
  private const int _CATEGORY_SIX_EXTRA_BITS_AT_EIGHT = 14;

  private int _ReadCoefficient(int token) {
    var category = Vp9Tables.TokenCategory[token];
    var extra = Vp9Tables.TokenExtraBits[token];
    int coefficient = Vp9Tables.TokenBaseValue[token];

    // Category six is the one token bit depth widens: fourteen extra bits at eight bits a sample,
    // sixteen at ten, eighteen at twelve, each read from its own tail of one table. Reading the
    // eight-bit fourteen out of a ten-bit stream leaves the arithmetic decoder two bits behind at
    // the first large coefficient and everything after it is noise.
    if (category == _CATEGORY_SIX) {
      var bits = _CATEGORY_SIX_EXTRA_BITS_AT_EIGHT + (this._header.BitDepth - 8);
      var probabilities = Vp9Tables.Category6Probabilities[(Vp9Tables.Category6Probabilities.Length - bits)..];
      for (var e = 0; e < bits; ++e)
        coefficient += this._reader.ReadBool(probabilities[e]) << (bits - 1 - e);

      return coefficient;
    }

    for (var e = 0; e < extra; ++e)
      coefficient += this._reader.ReadBool(Vp9Tables.CategoryProbabilities[category * 14 + e]) << (extra - 1 - e);

    return coefficient;
  }
}
