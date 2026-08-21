using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The quantisation weighting matrices — ITU-T H.265, clauses 7.3.4 and 7.4.5.
/// </summary>
/// <remarks>
/// A scaling list weights each transform coefficient separately before it is dequantised, so that the
/// quantiser can be coarser where the eye is less able to see the error. HEVC carries them for four
/// transform sizes and six matrices each — luma, Cb and Cr, intra and inter — and codes each one as
/// deltas along the same diagonal scan the coefficients themselves are read in, which is why the
/// values arrive in scan order and have to be scattered back into a square here.
/// <para/>
/// Three ways of saying what a matrix is, and all three are used by real encoders: coded explicitly,
/// copied from a matrix already sent, or the default from Tables 7-5 and 7-6. The default is also
/// what a stream means when it enables the lists and then sends none, which is not the same as
/// disabling them — the flat matrix a disabled stream uses is every entry 16, and the default is not
/// flat.
/// <para/>
/// The two largest sizes carry their DC coefficient separately, because the sixty-four coded values
/// of a 16x16 or 32x32 matrix are each replicated over a two-by-two or four-by-four square, and the
/// one at direct current is the one worth stating exactly.
/// </remarks>
internal sealed class H265ScalingList {

  /// <summary>Sizes 4x4, 8x8, 16x16 and 32x32, as clause 7.4.5 indexes them.</summary>
  private const int _SIZE_COUNT = 4;

  /// <summary>Luma, Cb and Cr, intra and inter.</summary>
  private const int _MATRIX_COUNT = 6;

  /// <summary>The coded values, in diagonal scan order: <c>[sizeId][matrixId][i]</c>.</summary>
  private readonly int[][][] _coded = new int[_SIZE_COUNT][][];

  /// <summary>The separately coded direct-current entries of the two largest sizes.</summary>
  private readonly int[][] _dc = [new int[_MATRIX_COUNT], new int[_MATRIX_COUNT]];

  /// <summary>The expanded square matrices: <c>[sizeId][matrixId][y * size + x]</c>.</summary>
  private readonly int[][][] _factors = new int[_SIZE_COUNT][][];

  /// <summary>
  /// The default 8x8 intra matrix of Table 7-6, in raster order.
  /// </summary>
  /// <remarks>
  /// Written as the square it is rather than as the sixty-four numbers the standard lists in scan
  /// order. The two are the same list — the standard's <c>i</c> runs along the diagonal scan — and
  /// the square is the one a reader can check against the shape it is meant to have: flat in the
  /// corner where the low frequencies are, rising towards the far corner where the eye stops caring.
  /// </remarks>
  private static readonly int[] _DefaultIntra8x8 = [
    16, 16, 16, 16, 17, 18, 21, 24,
    16, 16, 16, 16, 17, 19, 22, 25,
    16, 16, 17, 18, 20, 22, 25, 29,
    16, 16, 18, 21, 24, 27, 31, 36,
    17, 17, 20, 24, 30, 35, 41, 47,
    18, 19, 22, 27, 35, 44, 54, 65,
    21, 22, 25, 31, 41, 54, 70, 88,
    24, 25, 29, 36, 47, 65, 88, 115,
  ];

  /// <summary>The default 8x8 inter matrix of Table 7-6, in raster order.</summary>
  private static readonly int[] _DefaultInter8x8 = [
    16, 16, 16, 16, 17, 18, 20, 24,
    16, 16, 16, 17, 18, 20, 24, 25,
    16, 16, 17, 18, 20, 24, 25, 28,
    16, 17, 18, 20, 24, 25, 28, 33,
    17, 18, 20, 24, 25, 28, 33, 41,
    18, 20, 24, 25, 28, 33, 41, 54,
    20, 24, 25, 28, 33, 41, 54, 71,
    24, 25, 28, 33, 41, 54, 71, 91,
  ];

  private H265ScalingList() {
    for (var sizeId = 0; sizeId < _SIZE_COUNT; ++sizeId) {
      this._coded[sizeId] = new int[_MATRIX_COUNT][];
      this._factors[sizeId] = new int[_MATRIX_COUNT][];
    }
  }

  /// <summary>Every matrix at its Table 7-5 and 7-6 default, which is what an empty list means.</summary>
  internal static H265ScalingList Default() {
    var lists = new H265ScalingList();

    for (var sizeId = 0; sizeId < _SIZE_COUNT; ++sizeId)
      for (var matrixId = 0; matrixId < _MATRIX_COUNT; ++matrixId) {
        lists._coded[sizeId][matrixId] = _DefaultCoded(sizeId, matrixId);
        lists._dc[sizeId > 1 ? sizeId - 2 : 0][matrixId] = 16;
      }

    lists._Expand();
    return lists;
  }

  /// <summary>Reads a <c>scaling_list_data()</c> structure — clause 7.3.4.</summary>
  internal static H265ScalingList Parse(ref H265BitReader reader) {
    var lists = new H265ScalingList();

    for (var sizeId = 0; sizeId < _SIZE_COUNT; ++sizeId)
      // The 32x32 matrices exist only for luma and for the chroma format that codes chroma at full
      // resolution, so the standard steps this loop by three there and leaves the four it skips to
      // be filled in from the two it read.
      for (var matrixId = 0; matrixId < _MATRIX_COUNT; matrixId += sizeId == 3 ? 3 : 1) {
        if (!reader.ReadFlag()) {
          var delta = reader.ReadUnsignedExpGolomb();
          if (delta == 0) {
            lists._coded[sizeId][matrixId] = _DefaultCoded(sizeId, matrixId);
            lists._SetDc(sizeId, matrixId, 16);
            continue;
          }

          var referenceId = matrixId - delta * (sizeId == 3 ? 3 : 1);
          if (referenceId < 0)
            throw new InvalidDataException(
              $"An H.265 scaling list at size {sizeId} matrix {matrixId} copies from matrix {referenceId}, which is "
              + "before the first. scaling_list_pred_matrix_id_delta (clause 7.4.5) may only name a matrix already "
              + "read.");

          lists._coded[sizeId][matrixId] = (int[])lists._coded[sizeId][referenceId].Clone();
          lists._SetDc(sizeId, matrixId, lists._Dc(sizeId, referenceId));
          continue;
        }

        // The number of coded values is capped at sixty-four: the two largest matrices are coded as
        // 8x8 and each value replicated over the square it stands for.
        var count = Math.Min(64, 1 << (4 + (sizeId << 1)));
        var values = new int[count];
        var next = 8;

        if (sizeId > 1) {
          next = reader.ReadSignedExpGolomb() + 8;
          lists._SetDc(sizeId, matrixId, next);
        } else
          lists._SetDc(sizeId, matrixId, 16);

        for (var i = 0; i < count; ++i) {
          // The deltas wrap in a byte, so a matrix may climb past 255 and come back round rather
          // than being clamped there.
          next = (next + reader.ReadSignedExpGolomb() + 256) % 256;
          values[i] = next;
        }

        lists._coded[sizeId][matrixId] = values;
      }

    lists._FillSkipped();
    lists._Expand();
    return lists;
  }

  /// <summary>
  /// The weighting for one coefficient — <c>m[x][y]</c> of clause 8.6.3.
  /// </summary>
  /// <param name="log2Size">The transform block's size, as a base-two logarithm.</param>
  /// <param name="matrixId">Luma, Cb or Cr, offset by three for an inter block.</param>
  internal int Factor(int log2Size, int matrixId, int x, int y) {
    var sizeId = log2Size - 2;
    return this._factors[sizeId][matrixId][(y << log2Size) + x];
  }

  private int _Dc(int sizeId, int matrixId) => sizeId > 1 ? this._dc[sizeId - 2][matrixId] : 16;

  private void _SetDc(int sizeId, int matrixId, int value) {
    if (sizeId > 1)
      this._dc[sizeId - 2][matrixId] = value;
  }

  /// <summary>
  /// Gives the four 32x32 matrices the standard never codes the same values as the two it does.
  /// </summary>
  /// <remarks>
  /// Only luma intra and luma inter are transmitted at 32x32, because in every chroma format but
  /// 4:4:4 no chroma transform block is that big. They are filled in all the same so that the
  /// lookup is a plain index rather than a special case at the one place it would be asked.
  /// </remarks>
  private void _FillSkipped() {
    for (var matrixId = 0; matrixId < _MATRIX_COUNT; ++matrixId) {
      if (this._coded[3][matrixId] != null)
        continue;

      var source = matrixId < 3 ? 0 : 3;
      this._coded[3][matrixId] = (int[])this._coded[3][source].Clone();
      this._dc[1][matrixId] = this._dc[1][source];
    }
  }

  /// <summary>Scatters every coded list back into the square it weights — clause 7.4.5.</summary>
  private void _Expand() {
    for (var sizeId = 0; sizeId < _SIZE_COUNT; ++sizeId) {
      var log2Size = sizeId + 2;
      var size = 1 << log2Size;

      // The 4x4 matrices are coded one value per coefficient along the 4x4 scan; every larger one is
      // coded as sixty-four values along the 8x8 scan, each covering a square of the real matrix.
      var codedLog2 = sizeId == 0 ? 2 : 3;
      var replication = 1 << (log2Size - codedLog2);
      var scan = H265ScanOrder.Positions(codedLog2, H265ScanOrder.DIAGONAL);

      for (var matrixId = 0; matrixId < _MATRIX_COUNT; ++matrixId) {
        var values = this._coded[sizeId][matrixId];
        var factors = new int[size * size];

        for (var i = 0; i < values.Length; ++i) {
          var baseX = H265ScanOrder.X(scan, i) * replication;
          var baseY = H265ScanOrder.Y(scan, i) * replication;

          for (var j = 0; j < replication; ++j)
            for (var k = 0; k < replication; ++k)
              factors[((baseY + j) << log2Size) + baseX + k] = values[i];
        }

        if (sizeId > 1)
          factors[0] = this._dc[sizeId - 2][matrixId];

        this._factors[sizeId][matrixId] = factors;
      }
    }
  }

  /// <summary>The default list of Tables 7-5 and 7-6, in the diagonal scan order it is indexed by.</summary>
  private static int[] _DefaultCoded(int sizeId, int matrixId) {
    // Table 7-5: every 4x4 matrix is flat at sixteen. There is nothing to gain from weighting a
    // block that small, and a flat matrix is what a disabled scaling list means too.
    if (sizeId == 0) {
      var flat = new int[16];
      Array.Fill(flat, 16);
      return flat;
    }

    var raster = matrixId < 3 ? _DefaultIntra8x8 : _DefaultInter8x8;
    var scan = H265ScanOrder.Positions(3, H265ScanOrder.DIAGONAL);
    var scanned = new int[64];

    for (var i = 0; i < 64; ++i)
      scanned[i] = raster[(H265ScanOrder.Y(scan, i) << 3) + H265ScanOrder.X(scan, i)];

    return scanned;
  }
}
