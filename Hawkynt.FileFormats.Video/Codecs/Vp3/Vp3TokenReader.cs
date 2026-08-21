using System;
using System.IO;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Reads the quantised DCT coefficients of a whole frame (Section 7.7).
/// </summary>
/// <remarks>
/// The order is the surprising part: not block by block, but coefficient position by coefficient
/// position. The decoder makes sixty-four passes over the coded blocks, and on pass <i>n</i> it reads
/// whatever token comes next for every block that has not yet reached position <i>n</i>. Coefficients
/// at the same position across a frame are alike — the DC coefficients of neighbouring blocks are
/// close, and the high-frequency positions are nearly all zero everywhere — so grouping them lets one
/// end-of-block run cover hundreds of blocks at once, and lets the codebook change with the position.
/// <para/>
/// Which of the eighty codebooks reads a token depends on three things: whether the block is luma or
/// chroma, which of the five position groups of Table 7.42 the position falls in, and a four-bit
/// index the frame states — once for the DC position and once, covering all four AC groups, for the
/// rest. The AC indices are read even by a frame with no AC coefficients in it at all.
/// <para/>
/// Tokens do two kinds of thing. Those below seven end one or more blocks, filling the rest of each
/// with zeroes; the run can be as long as the whole remainder of the frame, and does not stop at the
/// end of a plane or of a pass. The rest write between one and sixty-four coefficients of the current
/// block: a value, a run of zeroes, or a run of zeroes and then a value.
/// <para/>
/// The count kept for each block is not simply how many coefficients it has. It is updated before
/// each token rather than after, which leaves a run of zeroes reaching all the way to position
/// sixty-three uncounted, and the specification is explicit that this is deliberate — VP3 counted
/// this way and the count decides whether the block is reconstructed by the full inverse transform or
/// by the DC-only shortcut, so counting it correctly would produce a different picture.
/// </remarks>
internal static class Vp3TokenReader {

  /// <summary>Tokens below this end blocks; the rest write coefficients.</summary>
  private const int _FIRST_COEFFICIENT_TOKEN = 7;

  /// <summary>How many codebooks each position group has to choose from.</summary>
  private const int _GROUP_SIZE = 16;

  /// <summary>The position group of each of the sixty-four coefficient positions, Table 7.42.</summary>
  private static readonly int[] _Group = _BuildGroups();

  private static int[] _BuildGroups() {
    var groups = new int[64];
    for (var position = 1; position < 64; ++position)
      groups[position] = position <= 5 ? 1 : position <= 14 ? 2 : position <= 27 ? 3 : 4;

    return groups;
  }

  /// <param name="coefficients">A <c>BlockCount &#215; 64</c> array to fill, in zig-zag order.</param>
  /// <param name="counts">The coefficient count of each block, which decides how it is reconstructed.</param>
  /// <param name="positions">Scratch: the next coefficient position of each block.</param>
  internal static void Read(
    Vp3BitReader reader, Vp3Geometry geometry, bool[] coded,
    short[] coefficients, byte[] counts, byte[] positions) {
    var blocks = geometry.BlockCount;
    var lumaBlocks = geometry.LumaBlockCount;

    for (var i = 0; i < blocks; ++i) {
      positions[i] = 0;
      counts[i] = 0;
    }

    coefficients.AsSpan(0, blocks * 64).Clear();

    var endOfBlockRun = 0;
    var lumaTable = 0;
    var chromaTable = 0;

    for (var position = 0; position < 64; ++position) {
      if (position <= 1) {
        lumaTable = reader.ReadBits(4);
        chromaTable = reader.ReadBits(4);
      }

      var group = _Group[position] * _GROUP_SIZE;

      for (var block = 0; block < blocks; ++block) {
        if (!coded[block] || positions[block] != position)
          continue;

        counts[block] = (byte)position;

        if (endOfBlockRun > 0) {
          positions[block] = 64;
          --endOfBlockRun;
          continue;
        }

        var table = Vp3HuffmanTables.All[group + (block < lumaBlocks ? lumaTable : chromaTable)];
        var token = table.Read(reader);

        if (token < _FIRST_COEFFICIENT_TOKEN) {
          endOfBlockRun = _ReadEndOfBlockRun(reader, token, coded, positions, blocks);
          counts[block] = positions[block];
          positions[block] = 64;
          --endOfBlockRun;
          continue;
        }

        _ReadCoefficients(reader, token, coefficients, counts, positions, block, position);
      }
    }

    if (endOfBlockRun != 0)
      throw new InvalidDataException(
        $"A VP3 frame ends inside an end-of-block run with {endOfBlockRun} blocks still to fill, so the run "
        + "reaches past the last block of the frame.");

    for (var block = 0; block < blocks; ++block)
      if (coded[block] && positions[block] != 64)
        throw new InvalidDataException(
          $"A VP3 coded block stopped at coefficient {positions[block]} of 64 without an end-of-block token. "
          + "The frame's tokens do not account for every coefficient of every coded block.");
  }

  /// <summary>
  /// Expands one of the seven end-of-block tokens into the number of blocks it ends, Section 7.7.1.
  /// </summary>
  /// <remarks>
  /// The last of the seven states its length in twelve bits and gives zero a meaning of its own: the
  /// rest of the frame. That is a length no VP3 encoder wrote, but its decoder read it, so it is here.
  /// </remarks>
  private static int _ReadEndOfBlockRun(
    Vp3BitReader reader, int token, bool[] coded, byte[] positions, int blocks) {
    switch (token) {
      case 0:
      case 1:
      case 2:
        return token + 1;
      case 3:
        return reader.ReadBits(2) + 4;
      case 4:
        return reader.ReadBits(3) + 8;
      case 5:
        return reader.ReadBits(4) + 16;
      default:
        var length = reader.ReadBits(12);
        if (length != 0)
          return length;

        for (var block = 0; block < blocks; ++block)
          if (coded[block] && positions[block] < 64)
            ++length;

        return length;
    }
  }

  /// <summary>
  /// Expands one of the twenty-five coefficient tokens, Section 7.7.2 and Table 7.38.
  /// </summary>
  /// <remarks>
  /// The tokens fall into four shapes: a run of zeroes and nothing else, a value, a run of zeroes
  /// followed by a one, and a run of zeroes followed by a two or a three. Only the first leaves the
  /// coefficient count alone.
  /// </remarks>
  private static void _ReadCoefficients(
    Vp3BitReader reader, int token, short[] coefficients, byte[] counts, byte[] positions,
    int block, int position) {
    var at = block * 64;
    int run;
    int magnitude;

    switch (token) {
      // A run of zeroes, which the coefficients array already holds, so only the position moves.
      case 7:
        positions[block] = _Advance(block, position, reader.ReadBits(3) + 1, 0);
        return;
      case 8:
        positions[block] = _Advance(block, position, reader.ReadBits(6) + 1, 0);
        return;

      // The four values small enough that their sign is part of the token.
      case 9:
        coefficients[at + position] = 1;
        break;
      case 10:
        coefficients[at + position] = -1;
        break;
      case 11:
        coefficients[at + position] = 2;
        break;
      case 12:
        coefficients[at + position] = -2;
        break;

      // Magnitudes three to six, sign in a bit of its own.
      case 13:
      case 14:
      case 15:
      case 16:
        magnitude = token - 10;
        coefficients[at + position] = (short)(reader.ReadBit() != 0 ? -magnitude : magnitude);
        break;

      // Magnitudes that need extra bits, in ranges that double as they grow.
      case 17:
      case 18:
      case 19:
      case 20:
      case 21:
      case 22:
        var (extraBits, first) = token switch {
          17 => (1, 7),
          18 => (2, 9),
          19 => (3, 13),
          20 => (4, 21),
          21 => (5, 37),
          _ => (9, 69),
        };
        var negative = reader.ReadBit() != 0;
        magnitude = reader.ReadBits(extraBits) + first;
        coefficients[at + position] = (short)(negative ? -magnitude : magnitude);
        break;

      // One to five zeroes and then a one.
      case 23:
      case 24:
      case 25:
      case 26:
      case 27:
        run = token - 22;
        positions[block] = _Advance(block, position, run, 1);
        coefficients[at + position + run] = (short)(reader.ReadBit() != 0 ? -1 : 1);
        counts[block] = positions[block];
        return;

      // Six to seventeen zeroes and then a one.
      case 28:
      case 29:
        negative = reader.ReadBit() != 0;
        run = token == 28 ? reader.ReadBits(2) + 6 : reader.ReadBits(3) + 10;
        positions[block] = _Advance(block, position, run, 1);
        coefficients[at + position + run] = (short)(negative ? -1 : 1);
        counts[block] = positions[block];
        return;

      // One zero, or two or three, and then a two or a three.
      case 30:
      case 31:
        negative = reader.ReadBit() != 0;
        magnitude = reader.ReadBit() + 2;
        run = token == 30 ? 1 : reader.ReadBit() + 2;
        positions[block] = _Advance(block, position, run, 1);
        coefficients[at + position + run] = (short)(negative ? -magnitude : magnitude);
        counts[block] = positions[block];
        return;
    }

    positions[block] = _Advance(block, position, 0, 1);
    counts[block] = positions[block];
  }

  /// <summary>
  /// Where a token leaves the block's next coefficient position, refusing one that runs off the end.
  /// </summary>
  /// <remarks>
  /// A block has sixty-four coefficients and a token that would write past the last of them is not
  /// something a valid stream contains. Left unchecked it would write into the next block's
  /// coefficients, which is the buffer overflow Section 7.7.2 warns implementers about, and would
  /// then decode into a picture rather than into an error.
  /// </remarks>
  private static byte _Advance(int block, int position, int zeroes, int values) {
    var next = position + zeroes + values;
    if (next > 64)
      throw new InvalidDataException(
        $"A VP3 token at coefficient {position} of block {block} writes {zeroes + values} more, which runs past "
        + "the 64 coefficients a block has.");

    return (byte)next;
  }
}
