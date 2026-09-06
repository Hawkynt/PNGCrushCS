using System;
using System.Collections.Generic;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The write direction of ITU-T H.261's variable-length code tables: a value in, its codeword out.
/// </summary>
/// <remarks>
/// Every codeword here is read back out of the very tables the decoder decodes with
/// (<see cref="H261VlcTables"/>), never transcribed a second time. A table typed twice is a table that
/// can disagree with itself, and the disagreement would show as an encoder writing something its own
/// decoder reads as a different symbol — the one failure a round trip through this library alone
/// cannot detect, because both halves would be wrong in the same way. Inverting the one transcription
/// makes that impossible by construction: if a code is wrong it is wrong in both directions and the
/// comparison against another implementation catches it.
/// <para/>
/// Construction is a check of its own. A value the table gives two codewords for would be a table
/// with no single answer to "how is this written", so it throws rather than silently picking one.
/// </remarks>
internal sealed class H261VlcWriter {

  private readonly Dictionary<int, (int Code, int Length)> _codes = [];
  private readonly string _name;

  private H261VlcWriter(H263VlcTable table) {
    this._name = table.Name;

    foreach (var (code, value) in table.Entries) {
      var bits = code.Replace(" ", string.Empty);
      if (!this._codes.TryAdd(value, (Convert.ToInt32(bits, 2), bits.Length)))
        throw new InvalidOperationException(
          $"{table.Name}: the value {value} is written by two different codewords, so there is no one way to write it.");
    }
  }

  /// <summary>Table 1/H.261 (MBA), by absolute address or by difference from the last transmitted one.</summary>
  internal static readonly H261VlcWriter MacroblockAddress = new(H261VlcTables.MacroblockAddress);

  /// <summary>Table 2/H.261 (MTYPE), by index into <see cref="H261MacroblockType.All"/>.</summary>
  internal static readonly H261VlcWriter MacroblockType = new(H261VlcTables.MacroblockType);

  /// <summary>Table 3/H.261 (MVD), by the difference in whole pixels.</summary>
  internal static readonly H261VlcWriter MotionVectorDifference = new(H261VlcTables.MotionVectorDifference);

  /// <summary>Table 4/H.261 (CBP), by the six-bit pattern itself.</summary>
  internal static readonly H261VlcWriter CodedBlockPattern = new(H261VlcTables.CodedBlockPattern);

  /// <summary>Table 5/H.261 (TCOEFF) as the first coefficient of a block states it.</summary>
  internal static readonly H261VlcWriter CoefficientFirst = new(H261VlcTables.CoefficientFirst);

  /// <summary>Table 5/H.261 (TCOEFF) as every coefficient after the first states it.</summary>
  internal static readonly H261VlcWriter CoefficientNotFirst = new(H261VlcTables.CoefficientNotFirst);

  /// <summary>The codeword for one value, or a refusal naming the table that has none.</summary>
  internal (int Code, int Length) this[int value] => this._codes.TryGetValue(value, out var code)
    ? code
    : throw new InvalidOperationException($"{this._name} states no codeword for the value {value}.");
}
