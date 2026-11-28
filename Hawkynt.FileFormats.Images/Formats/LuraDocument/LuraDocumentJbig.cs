using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.LuraDocument;

/// <summary>
/// The bi-level image entities (BIE) of ITU-T T.82 (JBIG) that hold a LuraDocument mask, limited to the single
/// resolution layer every LuraDocument mask uses: <c>D = 0</c>, one bit plane.
/// </summary>
/// <remarks>
/// Implemented from T.82 itself (clauses 6.2 to 6.8). Within that layer everything the standard allows is read:
/// any stripe height, the two- and three-line templates, typical prediction, moves of the adaptive template pixel
/// (<c>ATMOVE</c>), a shortened image (<c>NEWLEN</c>), comments and stripes that reset their state (<c>SDRST</c>).
/// Differential layers and private deterministic-prediction tables are refused by name.
/// </remarks>
internal static class LuraDocumentJbig {

  private const byte _Escape = 0xFF;
  private const byte _Stuff = 0x00;
  private const byte _Reserve = 0x01;
  private const byte _StripeEnd = 0x02;
  private const byte _StripeReset = 0x03;
  private const byte _Abort = 0x04;
  private const byte _NewLength = 0x05;
  private const byte _AtMove = 0x06;
  private const byte _Comment = 0x07;

  private const int _HeaderSize = 20;
  private const long _MaxPixels = 1L << 31;
  private const int _OptionTwoLineTemplate = 0x40;
  private const int _OptionVariableLength = 0x20;
  private const int _OptionTypicalPrediction = 0x08;
  private const int _OptionDeterministicPrediction = 0x04;
  private const int _OptionPrivateTable = 0x02;
  private const int _OptionLastTable = 0x01;

  /// <summary>
  /// The context the "line is the same as the one above" pseudo-pixel reuses, T.82 Figures 11 and 12, in the bit order
  /// of <see cref="_Context"/>.
  /// </summary>
  private const int _TypicalContextThreeLine = 0x0E5;
  private const int _TypicalContextTwoLine = 0x195;

  /// <summary>Decodes a whole BIE to rows of bits, most significant bit first, each row padded to a byte.</summary>
  /// <returns>The packed rows and the image size, which a <c>NEWLEN</c> marker may have shortened.</returns>
  /// <exception cref="InvalidDataException">Not a single-layer, single-plane BIE, or a broken marker structure.</exception>
  public static (byte[] Rows, int Width, int Height) Decode(byte[] bie) {
    if (bie.Length < _HeaderSize)
      throw new InvalidDataException("The JBIG mask is shorter than its 20-byte header.");

    int dl = bie[0], d = bie[1], planes = bie[2];
    var width = _ReadInt(bie, 4);
    var height = _ReadInt(bie, 8);
    var linesPerStripe = _ReadInt(bie, 12);
    var maxShift = bie[16];
    var options = bie[19];
    if (dl != 0 || d != 0)
      throw new InvalidDataException($"The JBIG mask has differential resolution layers (DL={dl}, D={d}); only a single layer is supported.");
    if (planes != 1)
      throw new InvalidDataException($"The JBIG mask has {planes} bit planes; a mask has one.");
    if (width <= 0 || height <= 0 || linesPerStripe <= 0)
      throw new InvalidDataException($"The JBIG mask declares {width}x{height} with {linesPerStripe} lines per stripe.");
    if ((long)width * height > _MaxPixels)
      throw new InvalidDataException($"The JBIG mask declares {width}x{height}, more than this reader decodes.");

    var position = _HeaderSize;
    if ((options & (_OptionDeterministicPrediction | _OptionPrivateTable | _OptionLastTable)) == (_OptionDeterministicPrediction | _OptionPrivateTable))
      position += 1728; // A private deterministic-prediction table: it only ever applies to differential layers.

    var twoLine = (options & _OptionTwoLineTemplate) != 0;
    var typical = (options & _OptionTypicalPrediction) != 0;
    var stride = (width + 7) >> 3;
    var rows = new byte[(long)stride * height];
    var contexts = new LuraDocumentQmCoder.Contexts();
    var atX = 0;
    var atY = 0;
    var lineNotTypical = true;
    var top = 0;
    var line = 0;
    var moves = new List<(int Line, int X, int Y)>();

    while (line < height && position < bie.Length) {
      // Floating marker segments before the next stripe. A stripe whose coded data is empty starts with its own end marker.
      if (bie[position] == _Escape && position + 1 < bie.Length && bie[position + 1] is not (_Stuff or _StripeEnd or _StripeReset)) {
        var marker = bie[position + 1];
        switch (marker) {
          case _AtMove:
            _Need(bie, position, 8);
            var yAt = _ReadInt(bie, position + 2);
            var tx = (sbyte)bie[position + 6];
            var ty = bie[position + 7];
            if (tx > maxShift && tx != 0)
              throw new InvalidDataException($"The JBIG mask moves its adaptive pixel {tx} pixels, beyond the {maxShift} its header allows.");
            moves.Add((yAt, tx, ty));
            position += 8;
            continue;
          case _NewLength:
            _Need(bie, position, 6);
            height = Math.Min(height, _ReadInt(bie, position + 2));
            position += 6;
            continue;
          case _Comment:
            _Need(bie, position, 6);
            position += 6 + _ReadInt(bie, position + 2);
            continue;
          case _Abort:
            throw new InvalidDataException("The JBIG mask was aborted by its encoder.");
          default:
            throw new InvalidDataException($"The JBIG mask has a stray marker 0x{marker:X2} between stripes.");
        }
      }

      // One stripe: protected coded data up to ESC SDNORM or ESC SDRST, unstuffed on the way.
      var coded = new List<byte>();
      var reset = false;
      var ended = false;
      while (position < bie.Length) {
        var b = bie[position++];
        if (b != _Escape) {
          coded.Add(b);
          continue;
        }

        if (position >= bie.Length)
          break;
        var marker = bie[position++];
        if (marker == _Stuff) {
          coded.Add(_Escape);
          continue;
        }

        if (marker is _StripeEnd or _StripeReset) {
          reset = marker == _StripeReset;
          ended = true;
          break;
        }

        if (marker == _NewLength) {
          _Need(bie, position - 2, 6);
          height = Math.Min(height, _ReadInt(bie, position));
          position += 4;
          continue;
        }

        throw new InvalidDataException(marker == _Abort ? "The JBIG mask was aborted by its encoder." : $"The JBIG mask has marker 0x{marker:X2} inside a stripe.");
      }

      if (!ended)
        throw new InvalidDataException("The JBIG mask ends inside a stripe.");

      var data = coded.ToArray();
      var decoder = new LuraDocumentQmCoder.Decoder(data, 0, data.Length);
      var stripeEnd = (int)Math.Min((long)line + linesPerStripe, height);
      var stripeStart = line;
      for (; line < stripeEnd; ++line) {
        foreach (var move in moves)
          if (stripeStart + move.Line == line) {
            atX = move.X;
            atY = move.Y;
          }

        var rowOffset = (long)line * stride;
        if (typical) {
          var sameness = decoder.Decode(contexts, twoLine ? _TypicalContextTwoLine : _TypicalContextThreeLine);
          lineNotTypical = !(sameness != 0 ^ lineNotTypical);
          if (!lineNotTypical) {
            if (line > top)
              Array.Copy(rows, rowOffset - stride, rows, rowOffset, stride);
            continue;
          }
        }

        for (var x = 0; x < width; ++x) {
          var context = _Context(rows, stride, width, top, line, x, twoLine, atX, atY);
          if (decoder.Decode(contexts, context) != 0)
            rows[rowOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
      }

      moves.Clear();
      if (reset) {
        contexts.Reset();
        atX = 0;
        atY = 0;
        lineNotTypical = true;
        top = line;
      }
    }

    if (line < height)
      throw new InvalidDataException($"The JBIG mask stops after {line} of its {height} lines.");

    return (rows.LongLength == (long)stride * height ? rows : rows[..(stride * height)], width, height);
  }

  /// <summary>Encodes packed rows (most significant bit first, byte-padded) as a BIE: three-line template, typical prediction on.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rows, int width, int height) {
    var stride = (width + 7) >> 3;
    // The stripe height LuraTech's own encoder picks: a 35th of the picture, at least 2 and at most 128 lines.
    var linesPerStripe = Math.Clamp(height / 35, 2, 128);
    var output = new MemoryStream();
    var header = new byte[_HeaderSize];
    header[2] = 1;
    _WriteInt(header, 4, width);
    _WriteInt(header, 8, height);
    _WriteInt(header, 12, linesPerStripe);
    header[18] = 0x03;
    header[19] = _OptionTypicalPrediction;
    output.Write(header);

    var contexts = new LuraDocumentQmCoder.Contexts();
    var source = rows.ToArray();
    var lineNotTypicalAbove = true;
    for (var stripeStart = 0; stripeStart < height; stripeStart += linesPerStripe) {
      var encoder = new LuraDocumentQmCoder.Encoder();
      var stripeEnd = Math.Min(stripeStart + linesPerStripe, height);
      for (var line = stripeStart; line < stripeEnd; ++line) {
        var rowOffset = line * stride;
        var lineNotTypical = line == 0
          ? !_IsBlank(source.AsSpan(0, stride), width)
          : !_SameRow(source.AsSpan(rowOffset - stride, stride), source.AsSpan(rowOffset, stride), width);
        encoder.Encode(contexts, _TypicalContextThreeLine, lineNotTypical == lineNotTypicalAbove ? 1 : 0);
        lineNotTypicalAbove = lineNotTypical;
        if (!lineNotTypical)
          continue;

        for (var x = 0; x < width; ++x) {
          var bit = (source[rowOffset + (x >> 3)] >> (7 - (x & 7))) & 1;
          encoder.Encode(contexts, _Context(source, stride, width, 0, line, x, false, 0, 0), bit);
        }
      }

      foreach (var b in encoder.Finish()) {
        output.WriteByte(b);
        if (b == _Escape)
          output.WriteByte(_Stuff);
      }

      output.WriteByte(_Escape);
      output.WriteByte(_StripeEnd);
    }

    return output.ToArray();
  }

  /// <summary>
  /// The ten-pixel context of T.82 Figures 14 (three lines) and 15 (two lines), most significant bit first in the
  /// order the standard's typical-prediction contexts are written in. The adaptive pixel sits at
  /// <c>(x - atX, y - atY)</c>, or at its default place when both are zero.
  /// </summary>
  private static int _Context(byte[] rows, int stride, int width, int top, int y, int x, bool twoLine, int atX, int atY) {
    int Pixel(int px, int py) {
      if (px < 0 || px >= width || py < top)
        return 0;
      return (rows[(long)py * stride + (px >> 3)] >> (7 - (px & 7))) & 1;
    }

    var moved = atX != 0 || atY != 0;
    if (twoLine) {
      var a = moved ? Pixel(x - atX, y - atY) : Pixel(x + 2, y - 1);
      return Pixel(x - 3, y - 1) << 9 | Pixel(x - 2, y - 1) << 8 | Pixel(x - 1, y - 1) << 7 | Pixel(x, y - 1) << 6
             | Pixel(x + 1, y - 1) << 5 | a << 4
             | Pixel(x - 4, y) << 3 | Pixel(x - 3, y) << 2 | Pixel(x - 2, y) << 1 | Pixel(x - 1, y);
    }

    var at = moved ? Pixel(x - atX, y - atY) : Pixel(x + 2, y - 1);
    return Pixel(x - 1, y - 2) << 9 | Pixel(x, y - 2) << 8 | Pixel(x + 1, y - 2) << 7
           | Pixel(x - 2, y - 1) << 6 | Pixel(x - 1, y - 1) << 5 | Pixel(x, y - 1) << 4 | Pixel(x + 1, y - 1) << 3 | at << 2
           | Pixel(x - 2, y) << 1 | Pixel(x - 1, y);
  }

  private static bool _IsBlank(ReadOnlySpan<byte> row, int width) {
    for (var x = 0; x < width; ++x)
      if (((row[x >> 3] >> (7 - (x & 7))) & 1) != 0)
        return false;
    return true;
  }

  private static bool _SameRow(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int width) {
    for (var x = 0; x < width; ++x)
      if (((a[x >> 3] ^ b[x >> 3]) >> (7 - (x & 7)) & 1) != 0)
        return false;
    return true;
  }

  private static void _Need(byte[] data, int position, int length) {
    if (position + length > data.Length)
      throw new InvalidDataException("The JBIG mask ends inside a marker segment.");
  }

  private static int _ReadInt(byte[] data, int offset) {
    var value = (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
    return value > int.MaxValue ? int.MaxValue : (int)value;
  }

  private static void _WriteInt(byte[] data, int offset, int value) {
    data[offset] = (byte)(value >> 24);
    data[offset + 1] = (byte)(value >> 16);
    data[offset + 2] = (byte)(value >> 8);
    data[offset + 3] = (byte)value;
  }
}
