using System;

namespace FileFormat.Codecs.H263;

/// <summary>The Annex O variable-length codes used by B-picture macroblocks.</summary>
internal static class H263AnnexOVlc {

  /// <summary>Table O.1: MBTYPE VLC codes for B-pictures.</summary>
  internal static readonly H263VlcTable BidirectionalMacroblockType = new(
    "ITU-T H.263 Table O.1 (B-picture MBTYPE)",
    ("11", 0),
    ("0001", 1),
    ("100", 2),
    ("101", 3),
    ("00110", 4),
    ("010", 5),
    ("011", 6),
    ("00111", 7),
    ("00100", 8),
    ("00101", 9),
    ("00001", 10),
    ("000001", 11),
    ("0000001", 12),
    ("000000001", 13));

  /// <summary>Table O.4: coded block pattern for the two chrominance blocks.</summary>
  internal static readonly H263VlcTable ChromaPattern = new(
    "ITU-T H.263 Table O.4 (CBPC)",
    ("0", 0),
    ("10", 1),
    ("111", 2),
    ("110", 3));

  internal static H263VlcWriter.Code BidirectionalMacroblockTypeCode(int type) => type switch {
    0 => new(0b11, 2),
    1 => new(0b0001, 4),
    2 => new(0b100, 3),
    3 => new(0b101, 3),
    4 => new(0b00110, 5),
    5 => new(0b010, 3),
    6 => new(0b011, 3),
    7 => new(0b00111, 5),
    8 => new(0b00100, 5),
    9 => new(0b00101, 5),
    10 => new(0b00001, 5),
    11 => new(0b000001, 6),
    12 => new(0b0000001, 7),
    13 => new(0b000000001, 9),
    _ => throw new ArgumentOutOfRangeException(nameof(type)),
  };

  internal static H263VlcWriter.Code ChromaPatternCode(int pattern) => pattern switch {
    0 => new(0b0, 1),
    1 => new(0b10, 2),
    2 => new(0b111, 3),
    3 => new(0b110, 3),
    _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
  };
}
