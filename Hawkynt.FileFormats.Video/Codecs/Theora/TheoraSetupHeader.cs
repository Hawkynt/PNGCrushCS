using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// The third of Theora's setup headers: the quantisation matrices, the loop filter limits and the
/// eighty Huffman codes.
/// </summary>
/// <remarks>
/// Theora specification section 6.4. This is the header that makes Theora configurable, and it is
/// the one place the format spends its bits freely — no field in it is octet-aligned, because it is
/// read once per stream and never again.
/// <para/>
/// The quantisation matrices are not stored. What is stored is a set of base matrices, a pair of
/// scale tables, and a description of which base matrices apply over which ranges of the
/// quantisation index — from which a matrix for any of the 384 combinations of quantisation type,
/// colour plane and index is built by linear interpolation. That indirection is why a Theora stream
/// can carry a full quality scale in a few hundred bytes.
/// </remarks>
internal sealed class TheoraSetupHeader {

  /// <summary>The number of quantisation indices, which is also the length of every table here.</summary>
  private const int _QUANTISATION_INDICES = 64;

  /// <summary>The number of coefficients in a block, and so in a base matrix.</summary>
  private const int _COEFFICIENTS = 64;

  /// <summary>The number of quantisation types: intra-coded blocks and everything else — Table 3.1.</summary>
  private const int _QUANTISATION_TYPES = 2;

  private const int _PLANES = 3;

  /// <summary>The number of Huffman tables the header carries — section 3.1.3.</summary>
  internal const int HUFFMAN_TABLES = 80;

  /// <summary>The most base matrices the header may declare — section 6.4.2.</summary>
  private const int _MAX_BASE_MATRICES = 384;

  /// <summary>One loop filter limit per quantisation index — section 6.4.1.</summary>
  internal int[] LoopFilterLimits { get; } = new int[_QUANTISATION_INDICES];

  /// <summary>The scale applied to every AC coefficient of a base matrix, per quantisation index.</summary>
  internal int[] AcScale { get; } = new int[_QUANTISATION_INDICES];

  /// <summary>The scale applied to the DC coefficient of a base matrix, per quantisation index.</summary>
  internal int[] DcScale { get; } = new int[_QUANTISATION_INDICES];

  /// <summary>The base matrices, in natural coefficient order.</summary>
  internal int[][] BaseMatrices { get; private set; } = [];

  /// <summary>How many quant ranges each quantisation type and colour plane declares.</summary>
  internal int[,] RangeCounts { get; } = new int[_QUANTISATION_TYPES, _PLANES];

  /// <summary>The width in quantisation indices of each quant range.</summary>
  internal int[,][] RangeSizes { get; } = new int[_QUANTISATION_TYPES, _PLANES][];

  /// <summary>The base matrix at each quant range's endpoints; a range of <c>n</c> has <c>n + 1</c>.</summary>
  internal int[,][] RangeMatrices { get; } = new int[_QUANTISATION_TYPES, _PLANES][];

  /// <summary>The eighty Huffman codes for DCT tokens.</summary>
  internal TheoraHuffmanTable[] HuffmanTables { get; } = new TheoraHuffmanTable[HUFFMAN_TABLES];

  private TheoraSetupHeader() { }

  /// <summary>Reads the setup header out of a packet whose type byte has already been taken.</summary>
  internal static TheoraSetupHeader Read(TheoraBitReader reader) {
    var header = new TheoraSetupHeader();

    header._ReadLoopFilterLimits(reader);
    header._ReadQuantisationParameters(reader);

    for (var table = 0; table < HUFFMAN_TABLES; ++table)
      header.HuffmanTables[table] = TheoraHuffmanTable.Read(reader, table);

    reader.EnsureComplete("the setup header");
    return header;
  }

  /// <summary>
  /// Reads the sixty-four loop filter limits — section 6.4.1.
  /// </summary>
  /// <remarks>
  /// A width, then the values at that width. The limits are seven-bit quantities and the width is
  /// stated in three bits, so a table of small limits costs a fraction of a byte each.
  /// </remarks>
  private void _ReadLoopFilterLimits(TheoraBitReader reader) {
    var bits = (int)reader.ReadBits(3);
    for (var index = 0; index < _QUANTISATION_INDICES; ++index)
      this.LoopFilterLimits[index] = (int)reader.ReadBits(bits);
  }

  /// <summary>Reads the scale tables, the base matrices and the quant ranges — section 6.4.2.</summary>
  private void _ReadQuantisationParameters(TheoraBitReader reader) {
    var acBits = (int)reader.ReadBits(4) + 1;
    for (var index = 0; index < _QUANTISATION_INDICES; ++index)
      this.AcScale[index] = (int)reader.ReadBits(acBits);

    var dcBits = (int)reader.ReadBits(4) + 1;
    for (var index = 0; index < _QUANTISATION_INDICES; ++index)
      this.DcScale[index] = (int)reader.ReadBits(dcBits);

    var matrices = (int)reader.ReadBits(9) + 1;
    if (matrices > _MAX_BASE_MATRICES)
      throw new InvalidDataException(
        $"The setup header declares {matrices} base matrices, where the specification allows at most {_MAX_BASE_MATRICES}.");

    this.BaseMatrices = new int[matrices][];
    for (var matrix = 0; matrix < matrices; ++matrix) {
      var values = new int[_COEFFICIENTS];
      for (var coefficient = 0; coefficient < _COEFFICIENTS; ++coefficient)
        values[coefficient] = (int)reader.ReadBits(8);

      this.BaseMatrices[matrix] = values;
    }

    var matrixIndexBits = _Ilog(matrices - 1);

    for (var type = 0; type < _QUANTISATION_TYPES; ++type)
    for (var plane = 0; plane < _PLANES; ++plane) {
      // The first set is always written out; every later one may instead say "the same as one
      // already read", which is how a stream that quantises both chroma planes alike — as VP3 does —
      // pays for one description rather than two.
      var isNew = type > 0 || plane > 0 ? reader.ReadBit() : 1;
      if (isNew == 0) {
        var fromPreviousType = type > 0 ? reader.ReadBit() : 0;
        var (sourceType, sourcePlane) = fromPreviousType == 1
          ? (type - 1, plane)
          : ((3 * type + plane - 1) / 3, (plane + 2) % 3);

        this.RangeCounts[type, plane] = this.RangeCounts[sourceType, sourcePlane];
        this.RangeSizes[type, plane] = this.RangeSizes[sourceType, sourcePlane];
        this.RangeMatrices[type, plane] = this.RangeMatrices[sourceType, sourcePlane];
        continue;
      }

      var sizes = new int[_QUANTISATION_INDICES];
      var endpoints = new int[_QUANTISATION_INDICES + 1];
      var ranges = 0;
      var index = 0;

      endpoints[0] = (int)reader.ReadBits(matrixIndexBits);
      if (endpoints[0] >= matrices)
        throw new InvalidDataException(
          $"A quant range of the setup header names base matrix {endpoints[0]}, where only {matrices} are declared.");

      do {
        // The width of the field shrinks as the ranges fill up the scale, because a range can never
        // be wider than what is left of it.
        sizes[ranges] = (int)reader.ReadBits(_Ilog(62 - index)) + 1;
        index += sizes[ranges];
        ++ranges;

        endpoints[ranges] = (int)reader.ReadBits(matrixIndexBits);
        if (endpoints[ranges] >= matrices)
          throw new InvalidDataException(
            $"A quant range of the setup header names base matrix {endpoints[ranges]}, where only {matrices} are declared.");
      } while (index < 63);

      // The ranges must cover the scale exactly. One that overshoots describes a matrix for a
      // quantisation index that does not exist, and the stream is undecodable rather than nearly so.
      if (index > 63)
        throw new InvalidDataException(
          $"The quant ranges for quantisation type {type} of plane {plane} cover {index} indices, where they MUST cover exactly 63.");

      this.RangeCounts[type, plane] = ranges;
      this.RangeSizes[type, plane] = sizes;
      this.RangeMatrices[type, plane] = endpoints;
    }
  }

  /// <summary>The bits needed to hold a positive integer in two's complement, and zero for anything else.</summary>
  private static int _Ilog(int value) {
    var bits = 0;
    while (value > 0) {
      ++bits;
      value >>= 1;
    }

    return bits;
  }
}
