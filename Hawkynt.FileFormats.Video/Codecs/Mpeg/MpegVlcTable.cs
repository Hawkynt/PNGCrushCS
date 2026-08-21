using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// One of ISO/IEC 11172-2's variable-length code tables, held as written and read by lookup.
/// </summary>
/// <remarks>
/// The codes are given as the strings the standard prints them as — <c>"0000 0101 11"</c> — rather
/// than as a pair of numbers. That is on purpose: these tables are transcribed by eye from a printed
/// annex, a single wrong bit is invisible until a picture is wrong somewhere subtle, and a form that
/// can be read straight across from the page is the only defence that scales to the six hundred-odd
/// codes here. The spaces are grouping and are ignored.
/// <para/>
/// Construction is itself a check. Every code is expanded across the lookup array, and a cell written
/// twice means two codes where one is a prefix of the other — which no valid table has — so a
/// transcription slip that produces an ambiguity throws the first time the decoder is used rather
/// than decoding something plausible. What construction cannot catch is a code that is unique but
/// attached to the wrong value; that is what the comparison against a reference decoder is for.
/// </remarks>
internal sealed class MpegVlcTable {

  /// <summary>Length in bits of the code that fills each lookup cell, or zero where none does.</summary>
  private readonly byte[] _lengths;

  private readonly short[] _values;
  private readonly int _maxLength;
  private readonly string _name;
  private readonly (string Code, int Value)[] _entries;

  internal MpegVlcTable(string name, params (string Code, int Value)[] entries) {
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
  internal int Read(ref MpegBitReader reader) {
    var bits = reader.NextBits(this._maxLength);
    var length = this._lengths[bits];
    if (length == 0)
      throw new InvalidDataException(
        $"Bit {reader.BitPosition} of the picture holds {Convert.ToString(bits, 2).PadLeft(this._maxLength, '0')}, "
        + $"which is not a code in {this._name}.");

    reader.Skip(length);
    return this._values[bits];
  }

  private static string _Bits(string code) => code.Replace(" ", string.Empty);
}
