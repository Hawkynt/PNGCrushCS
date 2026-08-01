using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.SinclairBasic;

/// <summary>Assembles a Sinclair BASIC picture program from a <see cref="SinclairBasicFile"/>.</summary>
/// <remarks>
/// Writing one means writing a program. The ZX81 could not store a picture on its own, so a picture
/// was a BASIC listing whose PRINT statements put the block characters where they belonged, and
/// running it was how you looked at it — which makes this the one format here where the encoder
/// emits source rather than pixels.
/// <para/>
/// One line per screen row: PRINT AT row,0 followed by the row's thirty-two characters. That is
/// larger than a hand-written listing would be, and it is the shape the format's reader recognises
/// without any guessing about intent.
/// <para/>
/// PRINT AT reaches rows 0 to 21 and no further — the machine keeps the bottom two lines for what
/// is being typed, which is precisely why these programs carry a scroll routine to fill the last
/// one. This does not emit that routine, so a picture with anything in its bottom two rows is
/// refused rather than written with them quietly missing.
/// </remarks>
public static class SinclairBasicWriter {

  private const byte _Newline = 118;
  private const byte _Quote = 11;
  private const byte _Comma = 26;
  private const byte _Semicolon = 25;
  private const byte _Print = 245;
  private const byte _At = 193;
  private const byte _NumberMarker = 126;

  /// <summary>The code a quote takes inside a string, since a bare one would end it.</summary>
  private const byte _QuoteInString = 192;

  /// <summary>Bytes that must follow the last line: the terminator and room for the reader to look.</summary>
  private const int _TrailerSize = 8;

  /// <summary>The last row PRINT AT can reach; the two below it belong to the input area.</summary>
  public const int LastPrintableRow = 21;

  public static byte[] ToBytes(SinclairBasicFile file) {
    var screen = file.Screen ?? [];

    for (var at = (LastPrintableRow + 1) * Zx81Graphics.Columns; at < screen.Length; ++at)
      if (screen[at] != 0)
        throw new NotSupportedException(
          "A picture with anything below row 21 needs the scroll routine, which this does not write.");
    var program = new List<byte>(SinclairBasicFile.ProgramOffset + Zx81Graphics.ScreenSize * 2);

    // The saved memory image begins with the machine's own variables, which a picture does not use.
    program.AddRange(new byte[SinclairBasicFile.ProgramOffset]);

    for (var row = 0; row <= LastPrintableRow; ++row) {
      var statement = new List<byte> { _Print, _At };
      _WriteNumber(statement, row);
      statement.Add(_Comma);
      _WriteNumber(statement, 0);
      statement.Add(_Quote);

      for (var column = 0; column < Zx81Graphics.Columns; ++column) {
        var at = row * Zx81Graphics.Columns + column;
        var code = at < screen.Length ? screen[at] : (byte)0;

        statement.Add(code == _Quote ? _QuoteInString : code);
      }

      statement.Add(_Quote);

      // A trailing semicolon suppresses the line break, so the next PRINT AT decides where to draw
      // rather than inheriting a position.
      statement.Add(_Semicolon);
      statement.Add(_Newline);

      // The line number is big-endian and its length little-endian, which is how the machine wrote
      // them and not a choice this makes.
      var number = row + 1;
      program.Add((byte)(number >> 8));
      program.Add((byte)number);
      program.Add((byte)statement.Count);
      program.Add((byte)(statement.Count >> 8));
      program.AddRange(statement);
    }

    // A newline where a line number would be ends the program; the reader looks eight bytes ahead
    // before reading it, so there has to be that much left.
    program.Add(_Newline);
    program.AddRange(new byte[_TrailerSize - 1]);

    return program.ToArray();
  }

  /// <summary>
  /// Writes a number the way the machine stored one: the digits it was typed with, then a marker,
  /// then the five-byte float that supersedes them.
  /// </summary>
  /// <remarks>
  /// The float is exponent-then-mantissa with the leading one implied, so its place in the first
  /// mantissa byte carries the sign instead. Zero has no leading one to imply and is written as
  /// five zero bytes.
  /// </remarks>
  private static void _WriteNumber(List<byte> into, int value) {
    foreach (var digit in value.ToString(System.Globalization.CultureInfo.InvariantCulture))
      into.Add((byte)(28 + (digit - '0')));

    into.Add(_NumberMarker);

    if (value == 0) {
      into.AddRange(new byte[5]);
      return;
    }

    var bits = 0;
    for (var probe = value; probe != 0; probe >>= 1)
      ++bits;

    var mantissa = (uint)value << (32 - bits);

    into.Add((byte)(128 + bits));
    into.Add((byte)((mantissa >> 24) & 0x7F));
    into.Add((byte)(mantissa >> 16));
    into.Add((byte)(mantissa >> 8));
    into.Add((byte)mantissa);
  }
}
