using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// One of SMPTE 421M's variable-length code tables, held as the standard prints it and read by lookup.
/// </summary>
/// <remarks>
/// The standard gives each table as a numbered list of rows, each row a codeword and the number of
/// bits it occupies, and that is exactly how they are stored here: a flat run of pairs whose position
/// is the index the standard attaches meaning to. Some of these tables run to a hundred and eighty
/// rows, and holding them in the shape the page has them in is what makes checking one against its
/// page possible at all.
/// <para/>
/// Construction is itself the check. Every code is expanded across the lookup array, and a cell
/// written twice means two codes where one is a prefix of the other — which no valid table has — so a
/// slip that produces an ambiguity throws the first time a decoder is built rather than decoding
/// something plausible. The count of cells left unwritten is kept as well, since for all but two of
/// these tables it must be nought: a complete code fills its space exactly, and a table that does not
/// has lost a row.
/// </remarks>
internal sealed class Vc1VlcTable {

  /// <summary>
  /// How many bits the first lookup covers.
  /// </summary>
  /// <remarks>
  /// The DC differential tables reach twenty-six bits, and a flat table that wide is sixty-seven
  /// million cells for a hundred and twenty codes. Twelve bits resolves every code of every table used
  /// here in one step but those, and the handful that are longer share a few prefixes between them, so
  /// a second table hangs off each of those prefixes and nothing else pays for them.
  /// </remarks>
  private const int _PRIMARY_BITS = 12;

  private readonly byte[] _lengths;
  private readonly short[] _values;
  private readonly int[] _subTable;
  private readonly byte[][] _subLengths;
  private readonly short[][] _subValues;
  private readonly int _primaryBits;
  private readonly int _subBits;
  private readonly int _maxLength;
  private readonly string _name;

  /// <summary>Builds a table from the standard's codeword-and-length pairs.</summary>
  /// <param name="name">The table's name as the standard prints it, for refusals.</param>
  /// <param name="codes">Codeword and bit count in turn, one pair per index.</param>
  internal Vc1VlcTable(string name, ReadOnlySpan<int> codes) {
    if ((codes.Length & 1) != 0)
      throw new ArgumentException($"{name}: the table is a run of codeword-and-length pairs, so its length is even.", nameof(codes));

    this._name = name;
    this.Count = codes.Length >> 1;

    var maxLength = 0;
    for (var i = 1; i < codes.Length; i += 2)
      maxLength = Math.Max(maxLength, codes[i]);

    if (maxLength is <= 0 or > 31)
      throw new ArgumentException($"{name}: a code of {maxLength} bits is not one this table can hold.", nameof(codes));

    this._maxLength = maxLength;
    this._primaryBits = Math.Min(maxLength, _PRIMARY_BITS);
    this._subBits = maxLength - this._primaryBits;

    this._lengths = new byte[1 << this._primaryBits];
    this._values = new short[1 << this._primaryBits];
    this._subTable = new int[1 << this._primaryBits];
    Array.Fill(this._subTable, -1);

    // Which prefixes carry codes too long for the first lookup, so that a second table is built for
    // each of them and for no others.
    var subTables = 0;
    for (var index = 0; index < this.Count; ++index) {
      var length = codes[(index * 2) + 1];
      if (length <= this._primaryBits)
        continue;

      var prefix = codes[index * 2] >> (length - this._primaryBits);
      if (this._subTable[prefix] < 0)
        this._subTable[prefix] = subTables++;
    }

    this._subLengths = new byte[subTables][];
    this._subValues = new short[subTables][];
    for (var i = 0; i < subTables; ++i) {
      this._subLengths[i] = new byte[1 << this._subBits];
      this._subValues[i] = new short[1 << this._subBits];
    }

    var filled = 0L;
    for (var index = 0; index < this.Count; ++index) {
      var code = codes[index * 2];
      var length = codes[(index * 2) + 1];

      if (length <= 0 || (uint)code >= 1u << length)
        throw new ArgumentException($"{name}: entry {index} states the codeword {code} in {length} bit(s), which does not fit.", nameof(codes));

      if (length <= this._primaryBits) {
        var span = 1 << (this._primaryBits - length);
        var from = code << (this._primaryBits - length);

        for (var i = from; i < from + span; ++i) {
          if (this._lengths[i] != 0 || this._subTable[i] >= 0)
            throw new InvalidOperationException(
              $"{name}: entry {index} collides with one already in the table, so the two are not a prefix code.");

          this._lengths[i] = (byte)length;
          this._values[i] = (short)index;
        }

        filled += (long)span << this._subBits;
        continue;
      }

      var table = this._subTable[code >> (length - this._primaryBits)];
      var subSpan = 1 << (this._maxLength - length);
      var subFrom = (code << (this._maxLength - length)) & ((1 << this._subBits) - 1);

      for (var i = subFrom; i < subFrom + subSpan; ++i) {
        if (this._subLengths[table][i] != 0)
          throw new InvalidOperationException(
            $"{name}: entry {index} collides with one already in the table, so the two are not a prefix code.");

        this._subLengths[table][i] = (byte)length;
        this._subValues[table][i] = (short)index;
      }

      filled += subSpan;
    }

    this.UnusedCells = (int)Math.Min(int.MaxValue, ((long)1 << this._maxLength) - filled);
  }

  /// <summary>How many indices the table defines.</summary>
  internal int Count { get; }

  /// <summary>The longest code in the table, in bits.</summary>
  internal int MaxLength => this._maxLength;

  /// <summary>
  /// How much of the code space no codeword reaches, which is nought for a complete code.
  /// </summary>
  /// <remarks>
  /// Every table of the standard used here is complete but the two Mid Rate ones, which leave the
  /// nine-zero codeword unassigned. Keeping the count means a test can assert exactly that rather than
  /// asserting nothing.
  /// </remarks>
  internal int UnusedCells { get; }

  /// <summary>The table's name as the standard prints it, for refusals.</summary>
  internal string Name => this._name;

  /// <summary>Reads one code and returns the index the standard attaches to it.</summary>
  /// <exception cref="InvalidDataException">The next bits are a code the table does not define.</exception>
  internal int Read(ref Vc1BitReader reader) {
    var prefix = reader.Peek(this._primaryBits);
    var length = this._lengths[prefix];
    if (length != 0) {
      reader.Skip(length);
      return this._values[prefix];
    }

    var table = this._subTable[prefix];
    if (table >= 0) {
      var rest = reader.Peek(this._maxLength) & ((1 << this._subBits) - 1);
      length = this._subLengths[table][rest];
      if (length != 0) {
        reader.Skip(length);
        return this._subValues[table][rest];
      }
    }

    throw new InvalidDataException(
      $"Bit {reader.BitPosition} of the picture holds {Convert.ToString(prefix, 2).PadLeft(this._primaryBits, '0')}, "
      + $"which begins no code in {this._name}.");
  }
}
