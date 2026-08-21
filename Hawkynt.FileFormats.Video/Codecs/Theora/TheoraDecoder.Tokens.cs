using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// The token layer: the quantised DCT coefficients of every coded block in the frame.
/// </summary>
/// <remarks>
/// Theora specification section 7.7. This is where the format is least like the ones around it.
/// Tokens are grouped by *coefficient position* rather than by block: every block's DC token is
/// written, then every block's first AC token, and so on for all 64 positions. The reason is
/// statistical — the codebook can be chosen for the frequency band rather than averaged over a whole
/// block — and the consequence is that nothing about any block is finished until the last pass is
/// done.
/// <para/>
/// The alphabet has 32 tokens and they do not map one-to-one onto coefficients. One token may be a
/// coefficient, a run of zeros, a run of zeros followed by a coefficient, an end-of-block marker, or
/// a run of end-of-block markers covering several blocks at once. So each block carries its own
/// current position, tokens are read only for blocks whose position has reached the pass, and a
/// single end-of-block run may finish blocks scattered across the frame — and, if there are not
/// enough at this position, wrap round and finish blocks at the next one.
/// </remarks>
internal sealed partial class TheoraDecoder {

  /// <summary>Tokens below this are end-of-block markers; the rest carry coefficients.</summary>
  private const int _FIRST_COEFFICIENT_TOKEN = 7;

  /// <summary>The position past the last coefficient, which is what a finished block's index reads.</summary>
  private const int _BLOCK_FINISHED = 64;

  /// <summary>How many end-of-block markers the current run still has to place.</summary>
  private long _endOfBlockRun;

  /// <summary>Reads every coded block's coefficients — section 7.7.3.</summary>
  private void _ReadCoefficients(TheoraBitReader reader, TheoraGeometry geometry) {
    Array.Clear(this._coefficients);
    Array.Clear(this._tokenIndices);
    Array.Clear(this._coefficientCounts);
    this._endOfBlockRun = 0;

    var setup = this._setup!;
    var lumaTable = 0;
    var chromaTable = 0;

    for (var position = 0; position < 64; ++position) {
      // Two codebook choices are made: one before the DC pass and one before the first AC pass. The
      // second is read even by a frame with no non-zero AC coefficient anywhere in it.
      if (position <= 1) {
        lumaTable = (int)reader.ReadBits(4);
        chromaTable = (int)reader.ReadBits(4);
      }

      var group = TheoraTables.HuffmanGroupOf(position);

      for (var block = 0; block < geometry.BlockCount; ++block) {
        if (!this._coded[block] || this._tokenIndices[block] != position)
          continue;

        // Updated before anything else, even though it is already this value unless the previous
        // token was a pure zero run. The specification does it here deliberately, to reproduce VP3's
        // accounting: the count excludes the coefficients of a trailing zero run, and the count is
        // what decides whether the block can take the DC-only shortcut through the transform.
        this._coefficientCounts[block] = (byte)position;

        if (this._endOfBlockRun > 0) {
          this._FinishBlock(block, position);
          --this._endOfBlockRun;
          continue;
        }

        var table = 16 * group + (block < geometry.LumaBlockCount ? lumaTable : chromaTable);
        var token = setup.HuffmanTables[table].ReadToken(reader);

        if (token < _FIRST_COEFFICIENT_TOKEN)
          this._ReadEndOfBlockToken(reader, geometry, token, block, position);
        else
          this._ReadCoefficientToken(reader, token, block, position);
      }
    }

    // Every coded block must have been finished and every end-of-block run used up. A stream where
    // one has not is one this decoder has lost its place in, and the coefficients of every block
    // after that point are somebody else's bits.
    if (this._endOfBlockRun != 0)
      throw new InvalidDataException(
        $"The frame's tokens end with {this._endOfBlockRun} end-of-block markers still to place, so the coefficient passes did not line up with the coded blocks.");

    for (var block = 0; block < geometry.BlockCount; ++block)
      if (this._coded[block] && this._tokenIndices[block] != _BLOCK_FINISHED)
        throw new InvalidDataException(
          $"Coded block {block} ends the frame at coefficient {this._tokenIndices[block]} of {_BLOCK_FINISHED}.");
  }

  /// <summary>Fills the rest of a block with zeros and marks it done.</summary>
  private void _FinishBlock(int block, int position) {
    var at = block * 64;
    for (var index = position; index < 64; ++index)
      this._coefficients[at + index] = 0;

    this._tokenIndices[block] = _BLOCK_FINISHED;
  }

  /// <summary>
  /// Expands an end-of-block token into a run and ends the current block — section 7.7.1.
  /// </summary>
  /// <remarks>
  /// The run covers this block and the next several in coded order whose position has reached this
  /// pass — and, if there are not enough, it carries on into the next pass. Token 6 is the odd one:
  /// unlike the others it adds no offset to its twelve-bit run, and a run of zero means "every coded
  /// block that is not yet finished".
  /// </remarks>
  private void _ReadEndOfBlockToken(TheoraBitReader reader, TheoraGeometry geometry, int token, int block, int position) {
    long run = token switch {
      0 => 1,
      1 => 2,
      2 => 3,
      3 => reader.ReadBits(2) + 4,
      4 => reader.ReadBits(3) + 8,
      5 => reader.ReadBits(4) + 16,
      _ => reader.ReadBits(12),
    };

    if (token == 6 && run == 0) {
      run = 0;
      for (var other = 0; other < geometry.BlockCount; ++other)
        if (this._coded[other] && this._tokenIndices[other] < _BLOCK_FINISHED)
          ++run;
    }

    this._FinishBlock(block, position);
    this._endOfBlockRun = run - 1;
  }

  /// <summary>
  /// Expands a coefficient token into the coefficients it stands for — section 7.7.2, Table 7.38.
  /// </summary>
  /// <remarks>
  /// Three shapes, interleaved through the token values. Tokens 7 and 8 are pure runs of zeros and
  /// nothing else, and are the only ones that do not update the coefficient count. Tokens 9 to 22
  /// are a single coefficient, of growing magnitude and with a growing number of extra bits. Tokens
  /// 23 to 31 combine a short run of zeros with a coefficient after it, which is what most of a real
  /// block turns out to be.
  /// </remarks>
  private void _ReadCoefficientToken(TheoraBitReader reader, int token, int block, int position) {
    var at = block * 64;

    // A malformed stream can push the position past the end of the block. It is caught here rather
    // than left to the array, because the alternative is a buffer overrun from an invalid packet.
    void Zeros(int count) {
      _Ensure(position + count);
      for (var index = 0; index < count; ++index)
        this._coefficients[at + position + index] = 0;
    }

    void Coefficient(int offset, int value) {
      _Ensure(position + offset + 1);
      this._coefficients[at + position + offset] = (short)value;
    }

    void _Ensure(int end) {
      if (end > 64)
        throw new InvalidDataException(
          $"A token at coefficient {position} of block {block} states {end - position} coefficients, which runs past the 64 a block has.");
    }

    switch (token) {
      // Pure zero runs. The coefficient count is deliberately not advanced: it is what decides
      // whether the block takes the DC-only path through the transform, and VP3 counts it this way.
      case 7: {
        var run = (int)reader.ReadBits(3) + 1;
        Zeros(run);
        this._tokenIndices[block] += (byte)run;
        return;
      }

      case 8: {
        var run = (int)reader.ReadBits(6) + 1;
        Zeros(run);
        this._tokenIndices[block] += (byte)run;
        return;
      }

      // The four smallest magnitudes, whose sign is in the token rather than in an extra bit.
      case 9: Coefficient(0, 1); break;
      case 10: Coefficient(0, -1); break;
      case 11: Coefficient(0, 2); break;
      case 12: Coefficient(0, -2); break;

      // Magnitudes 3 to 6, each with a sign bit.
      case >= 13 and <= 16: Coefficient(0, _Sign(reader, token - 10)); break;

      // Magnitude ranges, each with a sign bit — read first — and then a widening magnitude field.
      case 17: Coefficient(0, _Magnitude(reader, 7, 1)); break;
      case 18: Coefficient(0, _Magnitude(reader, 9, 2)); break;
      case 19: Coefficient(0, _Magnitude(reader, 13, 3)); break;
      case 20: Coefficient(0, _Magnitude(reader, 21, 4)); break;
      case 21: Coefficient(0, _Magnitude(reader, 37, 5)); break;
      case 22: Coefficient(0, _Magnitude(reader, 69, 9)); break;

      // One to five zeros and then a magnitude of one.
      case >= 23 and <= 27: {
        var run = token - 22;
        Zeros(run);
        Coefficient(run, _Sign(reader, 1));
        this._tokenIndices[block] += (byte)(run + 1);
        this._coefficientCounts[block] = this._tokenIndices[block];
        return;
      }

      // Six to seventeen zeros and then a magnitude of one.
      case 28 or 29: {
        var negative = reader.ReadBit() == 1;
        var run = token == 28 ? (int)reader.ReadBits(2) + 6 : (int)reader.ReadBits(3) + 10;
        Zeros(run);
        Coefficient(run, negative ? -1 : 1);
        this._tokenIndices[block] += (byte)(run + 1);
        this._coefficientCounts[block] = this._tokenIndices[block];
        return;
      }

      // One zero and then a magnitude of two or three.
      case 30: {
        var negative = reader.ReadBit() == 1;
        var magnitude = 2 + (int)reader.ReadBits(1);
        Zeros(1);
        Coefficient(1, negative ? -magnitude : magnitude);
        this._tokenIndices[block] += 2;
        this._coefficientCounts[block] = this._tokenIndices[block];
        return;
      }

      // Two or three zeros and then a magnitude of two or three.
      default: {
        var negative = reader.ReadBit() == 1;
        var magnitude = 2 + (int)reader.ReadBits(1);
        var run = 2 + (int)reader.ReadBits(1);
        Zeros(run);
        Coefficient(run, negative ? -magnitude : magnitude);
        this._tokenIndices[block] += (byte)(run + 1);
        this._coefficientCounts[block] = this._tokenIndices[block];
        return;
      }
    }

    ++this._tokenIndices[block];
    this._coefficientCounts[block] = this._tokenIndices[block];
  }

  /// <summary>Reads a sign bit and applies it to a magnitude.</summary>
  private static int _Sign(TheoraBitReader reader, int magnitude) => reader.ReadBit() == 1 ? -magnitude : magnitude;

  /// <summary>
  /// Reads a sign bit and then a magnitude field, in that order.
  /// </summary>
  /// <remarks>
  /// The order matters and is easy to get backwards: section 7.7.2 reads the sign first for every
  /// one of these tokens, and a decoder that read the magnitude first would take the sign bit out of
  /// the middle of it and come out one bit adrift for the rest of the frame.
  /// </remarks>
  private static int _Magnitude(TheoraBitReader reader, int smallest, int bits) {
    var negative = reader.ReadBit() == 1;
    var magnitude = smallest + (int)reader.ReadBits(bits);
    return negative ? -magnitude : magnitude;
  }
}
