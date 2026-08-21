using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// One of ITU-T H.264's variable-length code tables, held as written and read by lookup.
/// </summary>
/// <remarks>
/// The codes are given as the strings the standard prints them as — <c>"0000 0011 1"</c> — rather
/// than as a pair of numbers, so that each line can be read straight across from the page it was
/// taken off. There are some six hundred codes across clause 9.2's tables and a single wrong bit in
/// one of them does not fail: it decodes a different number of coefficients and the picture comes
/// out plausible and wrong. A form that can be proofread is the only defence that scales.
/// <para/>
/// Construction is itself a check. Each code is expanded across the lookup array, and a cell written
/// twice means one code is a prefix of another — which no valid table has — so a transcription slip
/// that creates an ambiguity throws the first time the table is built rather than decoding something
/// plausible. What that cannot catch is a code that is unique but attached to the wrong value, which
/// is what the comparison against a reference decoder is for.
/// </remarks>
internal sealed class H264VlcTable {

  /// <summary>Length in bits of the code filling each lookup cell, or zero where none does.</summary>
  private readonly byte[] _lengths;

  private readonly short[] _values;
  private readonly int _maxLength;
  private readonly string _name;
  private readonly (string Code, int Value)[] _entries;

  internal H264VlcTable(string name, params (string Code, int Value)[] entries) {
    ArgumentNullException.ThrowIfNull(entries);

    this._name = name;
    this._entries = entries;

    var maxLength = 0;
    foreach (var (code, _) in entries)
      maxLength = Math.Max(maxLength, _Bits(code).Length);

    this._maxLength = maxLength;
    this._lengths = new byte[1 << maxLength];
    this._values = new short[1 << maxLength];

    foreach (var (code, value) in entries) {
      if (value is < short.MinValue or > short.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(entries), $"{name}: the value {value} does not fit the lookup.");

      var bits = _Bits(code);
      var prefix = Convert.ToInt32(bits, 2);
      var span = 1 << (maxLength - bits.Length);
      var from = prefix << (maxLength - bits.Length);

      for (var i = from; i < from + span; ++i) {
        if (this._lengths[i] != 0)
          throw new InvalidOperationException(
            $"{name}: the code '{code}' collides with one already in the table, so the two are not a prefix code.");

        this._lengths[i] = (byte)bits.Length;
        this._values[i] = (short)value;
      }
    }
  }

  /// <summary>The table's codes and values, for the completeness checks that live in the tests.</summary>
  internal IReadOnlyList<(string Code, int Value)> Entries => this._entries;

  /// <summary>The longest code in the table, in bits.</summary>
  internal int MaxLength => this._maxLength;

  /// <summary>The table's name as the standard prints it, for refusals.</summary>
  internal string Name => this._name;

  /// <summary>Reads one code and returns the value the standard attaches to it.</summary>
  /// <exception cref="InvalidDataException">The next bits are a code the table does not define.</exception>
  internal int Read(ref H264BitReader reader) {
    var bits = reader.NextBits(this._maxLength);
    var length = this._lengths[bits];
    if (length == 0)
      throw new InvalidDataException(
        $"Bit {reader.BitPosition} of the slice holds {Convert.ToString(bits, 2).PadLeft(this._maxLength, '0')}, "
        + $"which is not a code in {this._name}.");

    reader.Skip(length);
    return this._values[bits];
  }

  private static string _Bits(string code) => code.Replace(" ", string.Empty);
}
