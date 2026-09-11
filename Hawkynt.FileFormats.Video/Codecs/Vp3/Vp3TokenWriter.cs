using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>Writes quantised VP3 DCT coefficients in the position-major order Section 7.7 defines.</summary>
/// <remarks>
/// This is intentionally a correctness encoder rather than an entropy optimiser. It selects built-in
/// codebook zero for luma and chroma in every coefficient group, uses an end-of-block token for each
/// block independently, and otherwise writes either a scalar coefficient or a zero run. Every form
/// is normative VP3 syntax; longer cross-block EOB runs and the combined run/value tokens can be
/// introduced later without changing the reconstructed picture.
/// </remarks>
internal static class Vp3TokenWriter {
  private const int _GROUP_SIZE = 16;

  internal static void Write(Vp3BitWriter writer, Vp3Geometry geometry, short[] coefficients) {
    var positions = new byte[geometry.BlockCount];

    for (var position = 0; position < 64; ++position) {
      if (position <= 1) {
        writer.WriteBits(0, 4); // luma table zero
        writer.WriteBits(0, 4); // chroma table zero
      }

      var table = Vp3HuffmanTables.All[_GroupOf(position) * _GROUP_SIZE];
      for (var block = 0; block < geometry.BlockCount; ++block) {
        if (positions[block] != position)
          continue;

        var at = block * 64;
        if (_AllZero(coefficients, at + position, 64 - position)) {
          table.Write(writer, 0); // end exactly this block
          positions[block] = 64;
          continue;
        }

        var value = coefficients[at + position];
        if (value == 0) {
          var run = 1;
          while (position + run < 64 && coefficients[at + position + run] == 0)
            ++run;

          if (run <= 8) {
            table.Write(writer, 7);
            writer.WriteBits((uint)(run - 1), 3);
          } else {
            table.Write(writer, 8);
            writer.WriteBits((uint)(run - 1), 6);
          }

          positions[block] = (byte)(position + run);
          continue;
        }

        _WriteValue(writer, table, value);
        positions[block] = (byte)(position + 1);
      }
    }
  }

  private static int _GroupOf(int position) => position switch {
    0 => 0,
    <= 5 => 1,
    <= 14 => 2,
    <= 27 => 3,
    _ => 4,
  };

  private static bool _AllZero(short[] coefficients, int offset, int count) {
    for (var i = 0; i < count; ++i)
      if (coefficients[offset + i] != 0)
        return false;

    return true;
  }

  private static void _WriteValue(Vp3BitWriter writer, Vp3VlcTable table, short value) {
    var negative = value < 0;
    var magnitude = negative ? -(int)value : value;

    switch (magnitude) {
      case 1:
        table.Write(writer, negative ? 10 : 9);
        return;
      case 2:
        table.Write(writer, negative ? 12 : 11);
        return;
      case <= 6:
        table.Write(writer, magnitude + 10);
        writer.WriteBit(negative ? 1 : 0);
        return;
      case <= 8:
        _WriteMagnitude(writer, table, 17, negative, magnitude - 7, 1);
        return;
      case <= 12:
        _WriteMagnitude(writer, table, 18, negative, magnitude - 9, 2);
        return;
      case <= 20:
        _WriteMagnitude(writer, table, 19, negative, magnitude - 13, 3);
        return;
      case <= 36:
        _WriteMagnitude(writer, table, 20, negative, magnitude - 21, 4);
        return;
      case <= 68:
        _WriteMagnitude(writer, table, 21, negative, magnitude - 37, 5);
        return;
      case <= 580:
        _WriteMagnitude(writer, table, 22, negative, magnitude - 69, 9);
        return;
      default:
        throw new InvalidOperationException(
          $"VP3 coefficient magnitude {magnitude} exceeds the largest scalar token, 580.");
    }
  }

  private static void _WriteMagnitude(
    Vp3BitWriter writer, Vp3VlcTable table, int token, bool negative, int extra, int extraBits) {
    table.Write(writer, token);
    writer.WriteBit(negative ? 1 : 0);
    writer.WriteBits((uint)extra, extraBits);
  }
}
