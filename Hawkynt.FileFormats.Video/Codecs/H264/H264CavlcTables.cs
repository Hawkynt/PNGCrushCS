using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The variable-length code tables of ITU-T H.264, clause 9.2 — the whole of CAVLC's alphabet.
/// </summary>
/// <remarks>
/// Five families of table, and which one is used is decided by the data rather than fixed.
/// <c>coeff_token</c> has six columns chosen by <c>nC</c>, an estimate of how many coefficients this
/// block will hold taken from the two blocks already decoded beside it (clause 9.2.1) — so a block in
/// a busy part of the picture is read out of a table whose short codes stand for large counts, and
/// one in a flat part out of a table whose short codes stand for none. <c>total_zeros</c> has fifteen
/// columns chosen by how many coefficients there turned out to be, and <c>run_before</c> seven chosen
/// by how many zeroes are left to place. Getting the column wrong reads a valid code and gets a
/// different number, which is why the choosing is done where the standard does it and not cached.
/// </remarks>
internal static class H264CavlcTables {

  /// <summary>Every table in this class, for the completeness checks that live in the tests.</summary>
  internal static IEnumerable<H264VlcTable> AllTables {
    get {
      yield return _CoeffToken0;
      yield return _CoeffToken2;
      yield return _CoeffToken4;
      yield return _CoeffToken8;
      yield return _CoeffTokenChromaDc;

      foreach (var table in _TotalZeros4x4)
        yield return table;

      foreach (var table in _TotalZerosChromaDc)
        yield return table;

      foreach (var table in _RunBefore)
        yield return table;
    }
  }

  /// <summary>Packs the two numbers <c>coeff_token</c> stands for into one table value.</summary>
  private static int _Token(int trailingOnes, int totalCoeff) => (totalCoeff << 2) | trailingOnes;

  /// <summary>The number of non-zero coefficients a <c>coeff_token</c> value stands for.</summary>
  internal static int TotalCoeff(int token) => token >> 2;

  /// <summary>How many of them are the trailing plus or minus ones.</summary>
  internal static int TrailingOnes(int token) => token & 3;

  // ============================================================================================
  // coeff_token — Table 9-5
  // ============================================================================================

  /// <summary>Table 9-5, column <c>0 &lt;= nC &lt; 2</c>.</summary>
  private static readonly H264VlcTable _CoeffToken0 = new("H.264 Table 9-5 coeff_token (0 <= nC < 2)",
    ("1", _Token(0, 0)),
    ("0001 01", _Token(0, 1)),
    ("01", _Token(1, 1)),
    ("0000 0111", _Token(0, 2)),
    ("0001 00", _Token(1, 2)),
    ("001", _Token(2, 2)),
    ("0000 0011 1", _Token(0, 3)),
    ("0000 0110", _Token(1, 3)),
    ("0000 101", _Token(2, 3)),
    ("0001 1", _Token(3, 3)),
    ("0000 0001 11", _Token(0, 4)),
    ("0000 0011 0", _Token(1, 4)),
    ("0000 0101", _Token(2, 4)),
    ("0000 11", _Token(3, 4)),
    ("0000 0000 111", _Token(0, 5)),
    ("0000 0001 10", _Token(1, 5)),
    ("0000 0010 1", _Token(2, 5)),
    ("0000 100", _Token(3, 5)),
    ("0000 0000 0111 1", _Token(0, 6)),
    ("0000 0000 110", _Token(1, 6)),
    ("0000 0001 01", _Token(2, 6)),
    ("0000 0100", _Token(3, 6)),
    ("0000 0000 0101 1", _Token(0, 7)),
    ("0000 0000 0111 0", _Token(1, 7)),
    ("0000 0000 101", _Token(2, 7)),
    ("0000 0010 0", _Token(3, 7)),
    ("0000 0000 0100 0", _Token(0, 8)),
    ("0000 0000 0101 0", _Token(1, 8)),
    ("0000 0000 0110 1", _Token(2, 8)),
    ("0000 0001 00", _Token(3, 8)),
    ("0000 0000 0011 11", _Token(0, 9)),
    ("0000 0000 0011 10", _Token(1, 9)),
    ("0000 0000 0100 1", _Token(2, 9)),
    ("0000 0000 100", _Token(3, 9)),
    ("0000 0000 0010 11", _Token(0, 10)),
    ("0000 0000 0010 10", _Token(1, 10)),
    ("0000 0000 0011 01", _Token(2, 10)),
    ("0000 0000 0110 0", _Token(3, 10)),
    ("0000 0000 0001 111", _Token(0, 11)),
    ("0000 0000 0001 110", _Token(1, 11)),
    ("0000 0000 0010 01", _Token(2, 11)),
    ("0000 0000 0011 00", _Token(3, 11)),
    ("0000 0000 0001 011", _Token(0, 12)),
    ("0000 0000 0001 010", _Token(1, 12)),
    ("0000 0000 0001 101", _Token(2, 12)),
    ("0000 0000 0010 00", _Token(3, 12)),
    ("0000 0000 0000 1111", _Token(0, 13)),
    ("0000 0000 0000 001", _Token(1, 13)),
    ("0000 0000 0001 001", _Token(2, 13)),
    ("0000 0000 0001 100", _Token(3, 13)),
    ("0000 0000 0000 1011", _Token(0, 14)),
    ("0000 0000 0000 1110", _Token(1, 14)),
    ("0000 0000 0000 1101", _Token(2, 14)),
    ("0000 0000 0001 000", _Token(3, 14)),
    ("0000 0000 0000 0111", _Token(0, 15)),
    ("0000 0000 0000 1010", _Token(1, 15)),
    ("0000 0000 0000 1001", _Token(2, 15)),
    ("0000 0000 0000 1100", _Token(3, 15)),
    ("0000 0000 0000 0100", _Token(0, 16)),
    ("0000 0000 0000 0110", _Token(1, 16)),
    ("0000 0000 0000 0101", _Token(2, 16)),
    ("0000 0000 0000 1000", _Token(3, 16)));

  /// <summary>Table 9-5, column <c>2 &lt;= nC &lt; 4</c>.</summary>
  private static readonly H264VlcTable _CoeffToken2 = new("H.264 Table 9-5 coeff_token (2 <= nC < 4)",
    ("11", _Token(0, 0)),
    ("0010 11", _Token(0, 1)),
    ("10", _Token(1, 1)),
    ("0001 11", _Token(0, 2)),
    ("0011 1", _Token(1, 2)),
    ("011", _Token(2, 2)),
    ("0000 111", _Token(0, 3)),
    ("0010 10", _Token(1, 3)),
    ("0010 01", _Token(2, 3)),
    ("0101", _Token(3, 3)),
    ("0000 0111", _Token(0, 4)),
    ("0001 10", _Token(1, 4)),
    ("0001 01", _Token(2, 4)),
    ("0100", _Token(3, 4)),
    ("0000 0100", _Token(0, 5)),
    ("0000 110", _Token(1, 5)),
    ("0000 101", _Token(2, 5)),
    ("0011 0", _Token(3, 5)),
    ("0000 0011 1", _Token(0, 6)),
    ("0000 0110", _Token(1, 6)),
    ("0000 0101", _Token(2, 6)),
    ("0010 00", _Token(3, 6)),
    ("0000 0001 111", _Token(0, 7)),
    ("0000 0011 0", _Token(1, 7)),
    ("0000 0010 1", _Token(2, 7)),
    ("0001 00", _Token(3, 7)),
    ("0000 0001 011", _Token(0, 8)),
    ("0000 0001 110", _Token(1, 8)),
    ("0000 0001 101", _Token(2, 8)),
    ("0000 100", _Token(3, 8)),
    ("0000 0000 1111", _Token(0, 9)),
    ("0000 0001 010", _Token(1, 9)),
    ("0000 0001 001", _Token(2, 9)),
    ("0000 0010 0", _Token(3, 9)),
    ("0000 0000 1011", _Token(0, 10)),
    ("0000 0000 1110", _Token(1, 10)),
    ("0000 0000 1101", _Token(2, 10)),
    ("0000 0001 100", _Token(3, 10)),
    ("0000 0000 1000", _Token(0, 11)),
    ("0000 0000 1010", _Token(1, 11)),
    ("0000 0000 1001", _Token(2, 11)),
    ("0000 0001 000", _Token(3, 11)),
    ("0000 0000 0111 1", _Token(0, 12)),
    ("0000 0000 0111 0", _Token(1, 12)),
    ("0000 0000 0110 1", _Token(2, 12)),
    ("0000 0000 1100", _Token(3, 12)),
    ("0000 0000 0101 1", _Token(0, 13)),
    ("0000 0000 0101 0", _Token(1, 13)),
    ("0000 0000 0100 1", _Token(2, 13)),
    ("0000 0000 0110 0", _Token(3, 13)),
    ("0000 0000 0011 1", _Token(0, 14)),
    ("0000 0000 0010 11", _Token(1, 14)),
    ("0000 0000 0011 0", _Token(2, 14)),
    ("0000 0000 0100 0", _Token(3, 14)),
    ("0000 0000 0010 01", _Token(0, 15)),
    ("0000 0000 0010 00", _Token(1, 15)),
    ("0000 0000 0010 10", _Token(2, 15)),
    ("0000 0000 0000 1", _Token(3, 15)),
    ("0000 0000 0001 11", _Token(0, 16)),
    ("0000 0000 0001 10", _Token(1, 16)),
    ("0000 0000 0001 01", _Token(2, 16)),
    ("0000 0000 0001 00", _Token(3, 16)));

  /// <summary>Table 9-5, column <c>4 &lt;= nC &lt; 8</c>.</summary>
  private static readonly H264VlcTable _CoeffToken4 = new("H.264 Table 9-5 coeff_token (4 <= nC < 8)",
    ("1111", _Token(0, 0)),
    ("0011 11", _Token(0, 1)),
    ("1110", _Token(1, 1)),
    ("0010 11", _Token(0, 2)),
    ("0111 1", _Token(1, 2)),
    ("1101", _Token(2, 2)),
    ("0010 00", _Token(0, 3)),
    ("0110 0", _Token(1, 3)),
    ("0111 0", _Token(2, 3)),
    ("1100", _Token(3, 3)),
    ("0001 111", _Token(0, 4)),
    ("0101 0", _Token(1, 4)),
    ("0101 1", _Token(2, 4)),
    ("1011", _Token(3, 4)),
    ("0001 011", _Token(0, 5)),
    ("0100 0", _Token(1, 5)),
    ("0100 1", _Token(2, 5)),
    ("1010", _Token(3, 5)),
    ("0001 001", _Token(0, 6)),
    ("0011 10", _Token(1, 6)),
    ("0011 01", _Token(2, 6)),
    ("1001", _Token(3, 6)),
    ("0001 000", _Token(0, 7)),
    ("0010 10", _Token(1, 7)),
    ("0010 01", _Token(2, 7)),
    ("1000", _Token(3, 7)),
    ("0000 1111", _Token(0, 8)),
    ("0001 110", _Token(1, 8)),
    ("0001 101", _Token(2, 8)),
    ("0110 1", _Token(3, 8)),
    ("0000 1011", _Token(0, 9)),
    ("0000 1110", _Token(1, 9)),
    ("0001 010", _Token(2, 9)),
    ("0011 00", _Token(3, 9)),
    ("0000 0111 1", _Token(0, 10)),
    ("0000 1010", _Token(1, 10)),
    ("0000 1101", _Token(2, 10)),
    ("0001 100", _Token(3, 10)),
    ("0000 0101 1", _Token(0, 11)),
    ("0000 0111 0", _Token(1, 11)),
    ("0000 1001", _Token(2, 11)),
    ("0000 1100", _Token(3, 11)),
    ("0000 0100 0", _Token(0, 12)),
    ("0000 0101 0", _Token(1, 12)),
    ("0000 0110 1", _Token(2, 12)),
    ("0000 1000", _Token(3, 12)),
    ("0000 0011 01", _Token(0, 13)),
    ("0000 0011 1", _Token(1, 13)),
    ("0000 0100 1", _Token(2, 13)),
    ("0000 0110 0", _Token(3, 13)),
    ("0000 0010 01", _Token(0, 14)),
    ("0000 0011 00", _Token(1, 14)),
    ("0000 0010 11", _Token(2, 14)),
    ("0000 0010 10", _Token(3, 14)),
    ("0000 0001 01", _Token(0, 15)),
    ("0000 0010 00", _Token(1, 15)),
    ("0000 0001 11", _Token(2, 15)),
    ("0000 0001 10", _Token(3, 15)),
    ("0000 0000 01", _Token(0, 16)),
    ("0000 0001 00", _Token(1, 16)),
    ("0000 0000 11", _Token(2, 16)),
    ("0000 0000 10", _Token(3, 16)));

  /// <summary>
  /// Table 9-5, column <c>8 &lt;= nC</c>: a six-bit fixed-length code rather than a variable one.
  /// </summary>
  /// <remarks>
  /// Written out in full rather than computed from <c>(TotalCoeff − 1) &lt;&lt; 2 | TrailingOnes</c>,
  /// which it happens to equal for every entry but the first. Two of the sixty-four codes stand for
  /// nothing, and a formula would silently accept them as counts of seventeen and nineteen
  /// coefficients where the table refuses them.
  /// </remarks>
  private static readonly H264VlcTable _CoeffToken8 = new("H.264 Table 9-5 coeff_token (8 <= nC)",
    ("0000 11", _Token(0, 0)),
    ("0000 00", _Token(0, 1)),
    ("0000 01", _Token(1, 1)),
    ("0001 00", _Token(0, 2)),
    ("0001 01", _Token(1, 2)),
    ("0001 10", _Token(2, 2)),
    ("0010 00", _Token(0, 3)),
    ("0010 01", _Token(1, 3)),
    ("0010 10", _Token(2, 3)),
    ("0010 11", _Token(3, 3)),
    ("0011 00", _Token(0, 4)),
    ("0011 01", _Token(1, 4)),
    ("0011 10", _Token(2, 4)),
    ("0011 11", _Token(3, 4)),
    ("0100 00", _Token(0, 5)),
    ("0100 01", _Token(1, 5)),
    ("0100 10", _Token(2, 5)),
    ("0100 11", _Token(3, 5)),
    ("0101 00", _Token(0, 6)),
    ("0101 01", _Token(1, 6)),
    ("0101 10", _Token(2, 6)),
    ("0101 11", _Token(3, 6)),
    ("0110 00", _Token(0, 7)),
    ("0110 01", _Token(1, 7)),
    ("0110 10", _Token(2, 7)),
    ("0110 11", _Token(3, 7)),
    ("0111 00", _Token(0, 8)),
    ("0111 01", _Token(1, 8)),
    ("0111 10", _Token(2, 8)),
    ("0111 11", _Token(3, 8)),
    ("1000 00", _Token(0, 9)),
    ("1000 01", _Token(1, 9)),
    ("1000 10", _Token(2, 9)),
    ("1000 11", _Token(3, 9)),
    ("1001 00", _Token(0, 10)),
    ("1001 01", _Token(1, 10)),
    ("1001 10", _Token(2, 10)),
    ("1001 11", _Token(3, 10)),
    ("1010 00", _Token(0, 11)),
    ("1010 01", _Token(1, 11)),
    ("1010 10", _Token(2, 11)),
    ("1010 11", _Token(3, 11)),
    ("1011 00", _Token(0, 12)),
    ("1011 01", _Token(1, 12)),
    ("1011 10", _Token(2, 12)),
    ("1011 11", _Token(3, 12)),
    ("1100 00", _Token(0, 13)),
    ("1100 01", _Token(1, 13)),
    ("1100 10", _Token(2, 13)),
    ("1100 11", _Token(3, 13)),
    ("1101 00", _Token(0, 14)),
    ("1101 01", _Token(1, 14)),
    ("1101 10", _Token(2, 14)),
    ("1101 11", _Token(3, 14)),
    ("1110 00", _Token(0, 15)),
    ("1110 01", _Token(1, 15)),
    ("1110 10", _Token(2, 15)),
    ("1110 11", _Token(3, 15)),
    ("1111 00", _Token(0, 16)),
    ("1111 01", _Token(1, 16)),
    ("1111 10", _Token(2, 16)),
    ("1111 11", _Token(3, 16)));

  /// <summary>Table 9-5, column <c>nC == −1</c>: the four-coefficient chroma DC block of 4:2:0.</summary>
  private static readonly H264VlcTable _CoeffTokenChromaDc = new("H.264 Table 9-5 coeff_token (nC == -1)",
    ("01", _Token(0, 0)),
    ("0001 11", _Token(0, 1)),
    ("1", _Token(1, 1)),
    ("0001 00", _Token(0, 2)),
    ("0001 10", _Token(1, 2)),
    ("001", _Token(2, 2)),
    ("0000 11", _Token(0, 3)),
    ("0000 011", _Token(1, 3)),
    ("0000 010", _Token(2, 3)),
    ("0001 01", _Token(3, 3)),
    ("0000 10", _Token(0, 4)),
    ("0000 0011", _Token(1, 4)),
    ("0000 0010", _Token(2, 4)),
    ("0000 000", _Token(3, 4)));

  // ============================================================================================
  // total_zeros — Tables 9-7, 9-8 and 9-9(a)
  // ============================================================================================

  /// <summary>Tables 9-7 and 9-8, indexed by <c>tzVlcIndex</c> minus one.</summary>
  private static readonly H264VlcTable[] _TotalZeros4x4 = [
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 1)",
      ("1", 0), ("011", 1), ("010", 2), ("0011", 3), ("0010", 4), ("0001 1", 5), ("0001 0", 6), ("0000 11", 7),
      ("0000 10", 8), ("0000 011", 9), ("0000 010", 10), ("0000 0011", 11), ("0000 0010", 12), ("0000 0001 1", 13),
      ("0000 0001 0", 14), ("0000 0000 1", 15)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 2)",
      ("111", 0), ("110", 1), ("101", 2), ("100", 3), ("011", 4), ("0101", 5), ("0100", 6), ("0011", 7),
      ("0010", 8), ("0001 1", 9), ("0001 0", 10), ("0000 11", 11), ("0000 10", 12), ("0000 01", 13),
      ("0000 00", 14)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 3)",
      ("0101", 0), ("111", 1), ("110", 2), ("101", 3), ("0100", 4), ("0011", 5), ("100", 6), ("011", 7),
      ("0010", 8), ("0001 1", 9), ("0001 0", 10), ("0000 01", 11), ("0000 1", 12), ("0000 00", 13)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 4)",
      ("0001 1", 0), ("111", 1), ("0101", 2), ("0100", 3), ("110", 4), ("101", 5), ("100", 6), ("0011", 7),
      ("011", 8), ("0010", 9), ("0001 0", 10), ("0000 1", 11), ("0000 0", 12)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 5)",
      ("0101", 0), ("0100", 1), ("0011", 2), ("111", 3), ("110", 4), ("101", 5), ("100", 6), ("011", 7),
      ("0010", 8), ("0000 1", 9), ("0001", 10), ("0000 0", 11)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 6)",
      ("0000 01", 0), ("0000 1", 1), ("111", 2), ("110", 3), ("101", 4), ("100", 5), ("011", 6), ("010", 7),
      ("0001", 8), ("001", 9), ("0000 00", 10)),
    new("H.264 Table 9-7 total_zeros (tzVlcIndex 7)",
      ("0000 01", 0), ("0000 1", 1), ("101", 2), ("100", 3), ("011", 4), ("11", 5), ("010", 6), ("0001", 7),
      ("001", 8), ("0000 00", 9)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 8)",
      ("0000 01", 0), ("0001", 1), ("0000 1", 2), ("011", 3), ("11", 4), ("10", 5), ("010", 6), ("001", 7),
      ("0000 00", 8)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 9)",
      ("0000 01", 0), ("0000 00", 1), ("0001", 2), ("11", 3), ("10", 4), ("001", 5), ("01", 6), ("0000 1", 7)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 10)",
      ("0000 1", 0), ("0000 0", 1), ("001", 2), ("11", 3), ("10", 4), ("01", 5), ("0001", 6)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 11)",
      ("0000", 0), ("0001", 1), ("001", 2), ("010", 3), ("1", 4), ("011", 5)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 12)",
      ("0000", 0), ("0001", 1), ("01", 2), ("1", 3), ("001", 4)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 13)",
      ("000", 0), ("001", 1), ("1", 2), ("01", 3)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 14)",
      ("00", 0), ("01", 1), ("1", 2)),
    new("H.264 Table 9-8 total_zeros (tzVlcIndex 15)",
      ("0", 0), ("1", 1)),
  ];

  /// <summary>Table 9-9 (a), the 2x2 chroma DC block of 4:2:0, indexed by <c>tzVlcIndex</c> minus one.</summary>
  private static readonly H264VlcTable[] _TotalZerosChromaDc = [
    new("H.264 Table 9-9(a) total_zeros (tzVlcIndex 1)", ("1", 0), ("01", 1), ("001", 2), ("000", 3)),
    new("H.264 Table 9-9(a) total_zeros (tzVlcIndex 2)", ("1", 0), ("01", 1), ("00", 2)),
    new("H.264 Table 9-9(a) total_zeros (tzVlcIndex 3)", ("1", 0), ("0", 1)),
  ];

  // ============================================================================================
  // run_before — Table 9-10
  // ============================================================================================

  /// <summary>
  /// Table 9-10, indexed by <c>zerosLeft</c> minus one, with everything above six in the last entry.
  /// </summary>
  private static readonly H264VlcTable[] _RunBefore = [
    new("H.264 Table 9-10 run_before (zerosLeft 1)", ("1", 0), ("0", 1)),
    new("H.264 Table 9-10 run_before (zerosLeft 2)", ("1", 0), ("01", 1), ("00", 2)),
    new("H.264 Table 9-10 run_before (zerosLeft 3)", ("11", 0), ("10", 1), ("01", 2), ("00", 3)),
    new("H.264 Table 9-10 run_before (zerosLeft 4)", ("11", 0), ("10", 1), ("01", 2), ("001", 3), ("000", 4)),
    new("H.264 Table 9-10 run_before (zerosLeft 5)",
      ("11", 0), ("10", 1), ("011", 2), ("010", 3), ("001", 4), ("000", 5)),
    new("H.264 Table 9-10 run_before (zerosLeft 6)",
      ("11", 0), ("000", 1), ("001", 2), ("011", 3), ("010", 4), ("101", 5), ("100", 6)),
    new("H.264 Table 9-10 run_before (zerosLeft > 6)",
      ("111", 0), ("110", 1), ("101", 2), ("100", 3), ("011", 4), ("010", 5), ("001", 6), ("0001", 7),
      ("0000 1", 8), ("0000 01", 9), ("0000 001", 10), ("0000 0001", 11), ("0000 0000 1", 12),
      ("0000 0000 01", 13), ("0000 0000 001", 14)),
  ];

  // ============================================================================================
  // coded_block_pattern — Table 9-4
  // ============================================================================================

  /// <summary>
  /// Table 9-4 (a): the coded block pattern each <c>me(v)</c> code number stands for, for
  /// <c>ChromaArrayType</c> 1 or 2. The first column is the intra reading and the second the inter
  /// reading of the same code number.
  /// </summary>
  private static readonly byte[,] _CodedBlockPattern = {
    { 47, 0 }, { 31, 16 }, { 15, 1 }, { 0, 2 }, { 23, 4 }, { 27, 8 }, { 29, 32 }, { 30, 3 },
    { 7, 5 }, { 11, 10 }, { 13, 12 }, { 14, 15 }, { 39, 47 }, { 43, 7 }, { 45, 11 }, { 46, 13 },
    { 16, 14 }, { 3, 6 }, { 5, 9 }, { 10, 31 }, { 12, 35 }, { 19, 37 }, { 21, 42 }, { 26, 44 },
    { 28, 33 }, { 35, 34 }, { 37, 36 }, { 42, 40 }, { 44, 39 }, { 1, 43 }, { 2, 45 }, { 4, 46 },
    { 8, 17 }, { 17, 18 }, { 18, 20 }, { 20, 24 }, { 24, 19 }, { 6, 21 }, { 9, 26 }, { 22, 28 },
    { 25, 23 }, { 32, 27 }, { 33, 29 }, { 34, 30 }, { 36, 22 }, { 40, 25 }, { 38, 38 }, { 41, 41 },
  };

  /// <summary>
  /// Reads <c>coeff_token</c> out of whichever of Table 9-5's columns <paramref name="nC"/> selects.
  /// </summary>
  internal static int ReadCoeffToken(ref H264BitReader reader, int nC) {
    var table = nC switch {
      -1 => _CoeffTokenChromaDc,
      < 0 => throw new NotSupportedException(
        "This H.264 stream codes a 2x4 chroma DC block (nC == -2, H.264 Table 9-5), which only 4:2:2 chroma "
        + "sampling produces. This decoder implements 4:2:0."),
      < 2 => _CoeffToken0,
      < 4 => _CoeffToken2,
      < 8 => _CoeffToken4,
      _ => _CoeffToken8,
    };

    return table.Read(ref reader);
  }

  /// <summary>Reads <c>total_zeros</c> for a block of up to sixteen coefficients (Tables 9-7 and 9-8).</summary>
  internal static int ReadTotalZeros4x4(ref H264BitReader reader, int totalCoeff)
    => _TotalZeros4x4[totalCoeff - 1].Read(ref reader);

  /// <summary>Reads <c>total_zeros</c> for a 2x2 chroma DC block (Table 9-9 (a)).</summary>
  internal static int ReadTotalZerosChromaDc(ref H264BitReader reader, int totalCoeff)
    => _TotalZerosChromaDc[totalCoeff - 1].Read(ref reader);

  /// <summary>Reads <c>run_before</c> for the given number of zeroes still to be placed (Table 9-10).</summary>
  internal static int ReadRunBefore(ref H264BitReader reader, int zerosLeft)
    => _RunBefore[Math.Min(zerosLeft, 7) - 1].Read(ref reader);

  /// <summary>
  /// Reads <c>coded_block_pattern</c>: an <c>me(v)</c> code number looked up in Table 9-4.
  /// </summary>
  /// <param name="intra">Which of the table's two columns applies, which is decided by the macroblock's prediction mode.</param>
  internal static int ReadCodedBlockPattern(ref H264BitReader reader, bool intra) {
    var codeNum = reader.ReadUnsignedExpGolomb();
    if (codeNum >= _CodedBlockPattern.GetLength(0))
      throw new InvalidDataException(
        $"An H.264 macroblock states coded_block_pattern code number {codeNum}, and H.264 Table 9-4 defines 0 to "
        + $"{_CodedBlockPattern.GetLength(0) - 1} for 4:2:0. The slice data is being read at the wrong bit position.");

    return _CodedBlockPattern[codeNum, intra ? 0 : 1];
  }
}
