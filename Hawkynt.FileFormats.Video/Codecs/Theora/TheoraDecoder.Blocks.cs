using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// The block layer: which blocks are coded, how their macro blocks predict, where they predict from,
/// and at which quantisation index.
/// </summary>
internal sealed partial class TheoraDecoder {

  /// <summary>
  /// Reads which blocks of the frame are coded — section 7.3.
  /// </summary>
  /// <remarks>
  /// Three run-length coded bit strings rather than one flag a block, and they nest. The first says
  /// which super blocks are *partially* coded; the second says, of the rest, which are fully coded
  /// and which are fully uncoded; the third gives a flag for each individual block of the partially
  /// coded ones. A frame where whole regions are still is therefore a handful of runs rather than a
  /// bit for every block in it.
  /// <para/>
  /// Flags are decoded for blocks lying entirely outside the picture region too. They are not shown,
  /// but they are real coded samples and later frames predict from them.
  /// </remarks>
  private void _ReadCodedBlockFlags(TheoraBitReader reader, TheoraGeometry geometry) {
    if (this._frameType == 0) {
      // Every block of an intra frame is coded, so the frame says nothing about it.
      Array.Fill(this._coded, true);
      return;
    }

    TheoraRunLength.ReadLong(reader, this._superBlockPartial, geometry.SuperBlockCount, "the partially coded super block flags");

    var wholeSuperBlocks = 0;
    for (var superBlock = 0; superBlock < geometry.SuperBlockCount; ++superBlock)
      if (!this._superBlockPartial[superBlock])
        ++wholeSuperBlocks;

    TheoraRunLength.ReadLong(reader, this._runBuffer, wholeSuperBlocks, "the fully coded super block flags");

    var taken = 0;
    for (var superBlock = 0; superBlock < geometry.SuperBlockCount; ++superBlock)
      if (!this._superBlockPartial[superBlock])
        this._superBlockFull[superBlock] = this._runBuffer[taken++];

    // A super block at the top or right edge of a plane holds fewer than sixteen blocks, so this is
    // not sixteen times the number of partially coded super blocks.
    var blocksInPartial = 0;
    for (var superBlock = 0; superBlock < geometry.SuperBlockCount; ++superBlock)
      if (this._superBlockPartial[superBlock])
        blocksInPartial += geometry.SuperBlockBlockCount[superBlock];

    // The short code, because a partially coded super block holds at most sixteen blocks and cannot
    // be all alike — so no run in this string can exceed thirty.
    TheoraRunLength.ReadShort(reader, this._runBuffer, blocksInPartial, "the coded block flags");

    taken = 0;
    for (var block = 0; block < geometry.BlockCount; ++block) {
      var superBlock = geometry.BlockSuperBlock[block];
      this._coded[block] = this._superBlockPartial[superBlock]
        ? this._runBuffer[taken++]
        : this._superBlockFull[superBlock];
    }
  }

  /// <summary>
  /// Reads a coding mode for each macro block that has a coded luma block — section 7.4.
  /// </summary>
  /// <remarks>
  /// A mode is stored only for a macro block with at least one coded *luma* block. One whose chroma
  /// blocks are coded and whose luma blocks are not gets INTER NOMV by definition, and nothing is
  /// read for it — which is why the coded block flags have to be decoded first.
  /// <para/>
  /// The eight modes are coded with one unary alphabet under eight schemes. Six of the schemes are
  /// fixed permutations of the modes, chosen by the encoder to put whichever modes it used most on
  /// the shortest codes; scheme 0 spells a permutation out here, and scheme 7 abandons the code and
  /// writes three flat bits a macro block.
  /// </remarks>
  private void _ReadMacroBlockModes(TheoraBitReader reader, TheoraGeometry geometry) {
    if (this._frameType == 0) {
      Array.Fill(this._modes, (byte)TheoraCodingMode.Intra);
      return;
    }

    var scheme = (int)reader.ReadBits(3);
    Span<byte> alphabet = stackalloc byte[8];

    if (scheme == 0)
      // The permutation is written the other way round from how it is used: for each mode in turn,
      // which code stands for it.
      for (var mode = 0; mode < 8; ++mode)
        alphabet[(int)reader.ReadBits(3)] = (byte)mode;
    else if (scheme != 7)
      TheoraTables.ModeSchemes[scheme - 1].CopyTo(alphabet);

    for (var macroBlock = 0; macroBlock < geometry.MacroBlockCount; ++macroBlock) {
      var anyLumaCoded = false;
      for (var corner = 0; corner < 4; ++corner)
        if (this._coded[geometry.MacroBlockLumaBlocks[macroBlock * 4 + corner]]) {
          anyLumaCoded = true;
          break;
        }

      if (!anyLumaCoded) {
        this._modes[macroBlock] = (byte)TheoraCodingMode.InterNoMotion;
        continue;
      }

      this._modes[macroBlock] = scheme == 7 ? (byte)reader.ReadBits(3) : alphabet[_ReadModeCode(reader)];
    }
  }

  /// <summary>Reads one of the eight unary mode codes of Table 7.19 and gives its position.</summary>
  /// <remarks>
  /// Ones until a zero, up to six of them; seven ones is the eighth code and carries no terminator,
  /// because there is nothing left for it to be distinguished from.
  /// </remarks>
  private static int _ReadModeCode(TheoraBitReader reader) {
    var index = 0;
    while (index < 7 && reader.ReadBit() == 1)
      ++index;

    return index;
  }

  /// <summary>
  /// Reads the motion vectors and assigns one to every coded block — section 7.5.
  /// </summary>
  /// <remarks>
  /// Most modes carry one vector for the whole macro block. Two carry none and mean the zero vector;
  /// two more carry none and mean "the last vector used" or "the one before that", which is the
  /// format's cheapest way of coding panning. Only INTER MV FOUR carries a vector per luma block,
  /// and there the chroma blocks' vectors are averaged from the luma ones according to how the
  /// chroma planes are sampled.
  /// <para/>
  /// The two remembered vectors count only vectors that point into the *previous* frame. A macro
  /// block coded in INTRA mode or against the golden frame does not disturb them, so a run of golden
  /// predictions in the middle of a pan does not lose the pan.
  /// </remarks>
  private void _ReadMotionVectors(TheoraBitReader reader, TheoraGeometry geometry) {
    Array.Clear(this._motionX);
    Array.Clear(this._motionY);

    if (this._frameType == 0)
      return;

    int lastX = 0, lastY = 0, secondLastX = 0, secondLastY = 0;

    // Which of the two ways a vector component is written is chosen once for the whole frame, and
    // the bit that says so is read whether or not any vector follows it.
    var compact = reader.ReadBit() == 0;

    for (var macroBlock = 0; macroBlock < geometry.MacroBlockCount; ++macroBlock) {
      var mode = (TheoraCodingMode)this._modes[macroBlock];
      int x = 0, y = 0;

      if (mode == TheoraCodingMode.InterMotionFour) {
        this._ReadFourMotionVectors(reader, geometry, macroBlock, compact, ref x, ref y);
        (secondLastX, secondLastY) = (lastX, lastY);
        (lastX, lastY) = (x, y);
        continue;
      }

      switch (mode) {
        case TheoraCodingMode.InterGoldenMotion:
          (x, y) = _ReadMotionVector(reader, compact);
          break;

        case TheoraCodingMode.InterMotionLast2:
          (x, y) = (secondLastX, secondLastY);
          (secondLastX, secondLastY) = (lastX, lastY);
          (lastX, lastY) = (x, y);
          break;

        case TheoraCodingMode.InterMotionLast:
          (x, y) = (lastX, lastY);
          break;

        case TheoraCodingMode.InterMotion:
          (x, y) = _ReadMotionVector(reader, compact);
          (secondLastX, secondLastY) = (lastX, lastY);
          (lastX, lastY) = (x, y);
          break;

        default:
          // INTER NOMV, INTRA and INTER GOLDEN NOMV all mean the zero vector, and none of them
          // disturbs the remembered ones.
          break;
      }

      // Every coded block of the macro block takes the vector, chroma as well as luma. Uncoded ones
      // keep the zero the arrays were cleared to, which is what the copy path uses for them anyway.
      var perPlane = geometry.ChromaBlocksPerMacroBlockPerPlane;
      for (var corner = 0; corner < 4; ++corner)
        this._SetMotion(geometry.MacroBlockLumaBlocks[macroBlock * 4 + corner], x, y);

      for (var slot = 0; slot < perPlane * 2; ++slot)
        this._SetMotion(geometry.MacroBlockChromaBlocks[macroBlock * 2 * perPlane + slot], x, y);
    }
  }

  private void _SetMotion(int block, int x, int y) {
    if (!this._coded[block])
      return;

    this._motionX[block] = (sbyte)x;
    this._motionY[block] = (sbyte)y;
  }

  /// <summary>
  /// Reads the four vectors of an INTER MV FOUR macro block and derives its chroma vectors.
  /// </summary>
  /// <param name="lastX">Receives the last vector actually read, which is what the frame remembers.</param>
  private void _ReadFourMotionVectors(
    TheoraBitReader reader, TheoraGeometry geometry, int macroBlock, bool compact, ref int lastX, ref int lastY) {
    Span<int> x = stackalloc int[4];
    Span<int> y = stackalloc int[4];

    // Raster order — lower-left, lower-right, upper-left, upper-right — and not coded order, which
    // is the one place in the frame layer the two differ and matter.
    for (var corner = 0; corner < 4; ++corner) {
      var block = geometry.MacroBlockLumaBlocks[macroBlock * 4 + corner];
      if (!this._coded[block]) {
        // An uncoded luma block takes the zero vector, which still counts towards the chroma
        // average: the premise is that the block has not moved.
        x[corner] = y[corner] = 0;
        continue;
      }

      (x[corner], y[corner]) = _ReadMotionVector(reader, compact);
      this._motionX[block] = (sbyte)x[corner];
      this._motionY[block] = (sbyte)y[corner];
      lastX = x[corner];
      lastY = y[corner];
    }

    var perPlane = geometry.ChromaBlocksPerMacroBlockPerPlane;
    for (var plane = 0; plane < 2; ++plane) {
      var at = (macroBlock * 2 + plane) * perPlane;

      switch (perPlane) {
        case 1:
          // 4:2:0: one chroma block covers the whole macro block, so it averages all four.
          this._SetChroma(geometry, at, _RoundDivide(x[0] + x[1] + x[2] + x[3], 4), _RoundDivide(y[0] + y[1] + y[2] + y[3], 4));
          break;

        case 2:
          // 4:2:2: two chroma blocks stacked, each averaging the two luma blocks beside it.
          this._SetChroma(geometry, at, _RoundDivide(x[0] + x[1], 2), _RoundDivide(y[0] + y[1], 2));
          this._SetChroma(geometry, at + 1, _RoundDivide(x[2] + x[3], 2), _RoundDivide(y[2] + y[3], 2));
          break;

        default:
          // 4:4:4: one chroma block per luma block, taking its vector unchanged.
          for (var corner = 0; corner < 4; ++corner)
            this._SetChroma(geometry, at + corner, x[corner], y[corner]);

          break;
      }
    }
  }

  /// <summary>Gives a derived chroma vector to one chroma block of an INTER MV FOUR macro block.</summary>
  /// <remarks>
  /// Unconditionally, unlike the single-vector modes, because that is what section 7.5.2 says: the
  /// derived vectors are assigned to the chroma blocks whether or not those blocks are coded. It
  /// makes no difference to the picture — an uncoded block is copied with the zero vector — and it
  /// is left as the specification has it rather than tidied.
  /// </remarks>
  private void _SetChroma(TheoraGeometry geometry, int slot, int x, int y) {
    var block = geometry.MacroBlockChromaBlocks[slot];
    this._motionX[block] = (sbyte)x;
    this._motionY[block] = (sbyte)y;
  }

  /// <summary>
  /// Reads one motion vector component pair — section 7.5.1.
  /// </summary>
  /// <remarks>
  /// Either a variable-length code from Table 7.23, which is a three-bit prefix and a magnitude and
  /// sign, or a flat five-bit magnitude with a sign bit. The flat form has two spellings of zero and
  /// reads a sign bit even for it, which is a VP3 compatibility rather than an oversight.
  /// </remarks>
  private static (int X, int Y) _ReadMotionVector(TheoraBitReader reader, bool compact) {
    if (!compact) {
      var magnitudeX = (int)reader.ReadBits(5);
      var negativeX = reader.ReadBit() == 1;
      var magnitudeY = (int)reader.ReadBits(5);
      var negativeY = reader.ReadBit() == 1;
      return (negativeX ? -magnitudeX : magnitudeX, negativeY ? -magnitudeY : magnitudeY);
    }

    return (_ReadMotionComponent(reader), _ReadMotionComponent(reader));
  }

  /// <summary>One component of a variable-length coded motion vector — Table 7.23.</summary>
  /// <remarks>
  /// A three-bit prefix decides how many magnitude bits follow: none for the four smallest values,
  /// then one, two, three and four bits for the ranges 2..3, 4..7, 8..15 and 16..31. Every value but
  /// zero carries a sign bit last.
  /// </remarks>
  private static int _ReadMotionComponent(TheoraBitReader reader) {
    var prefix = (int)reader.ReadBits(3);

    return prefix switch {
      0 => 0,
      1 => 1,
      2 => -1,
      3 => reader.ReadBit() == 1 ? -2 : 2,
      4 => reader.ReadBit() == 1 ? -3 : 3,
      5 => _Signed(4 + (int)reader.ReadBits(2)),
      6 => _Signed(8 + (int)reader.ReadBits(3)),
      _ => _Signed(16 + (int)reader.ReadBits(4)),
    };

    int _Signed(int magnitude) => reader.ReadBit() == 1 ? -magnitude : magnitude;
  }

  /// <summary>Divides, rounding to nearest with ties away from zero — the specification's <c>round</c>.</summary>
  private static int _RoundDivide(int sum, int divisor)
    => sum >= 0
      ? (2 * sum + divisor) / (2 * divisor)
      : -((-2 * sum + divisor) / (2 * divisor));

  /// <summary>
  /// Reads which of the frame's quantisation indices each coded block uses — section 7.6.
  /// </summary>
  /// <remarks>
  /// Nothing is read at all where the frame declared one index, which is the ordinary case and the
  /// only one VP3 has. Where it declared two or three, one run-length coded bit string is read per
  /// index but the last, each one dividing the blocks still undecided into those that use this index
  /// and those that use a later one — so the second string covers only the blocks the first left
  /// over.
  /// <para/>
  /// This selects the index for the AC coefficients alone. Every DC coefficient in the frame uses
  /// the first index, because DC prediction happens in the quantised domain and a neighbour
  /// quantised on another scale would predict a value on that scale.
  /// </remarks>
  private void _ReadBlockQuantisationIndices(TheoraBitReader reader, TheoraGeometry geometry) {
    Array.Clear(this._quantisationIndices);

    for (var index = 0; index < this._quantisationIndexCount - 1; ++index) {
      var undecided = 0;
      for (var block = 0; block < geometry.BlockCount; ++block)
        if (this._coded[block] && this._quantisationIndices[block] == index)
          ++undecided;

      TheoraRunLength.ReadLong(reader, this._runBuffer, undecided, "the block-level quantisation indices");

      var taken = 0;
      for (var block = 0; block < geometry.BlockCount; ++block)
        if (this._coded[block] && this._quantisationIndices[block] == index && this._runBuffer[taken++])
          ++this._quantisationIndices[block];
    }
  }
}
