namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Reads the coding mode of each macro block and the motion vector of each block (Sections 7.4
/// and 7.5).
/// </summary>
/// <remarks>
/// A macro block's mode says which reference frame its six blocks predict from and where the vector
/// that points into it comes from. Only a macro block with at least one coded luma block has a mode
/// in the bitstream at all; one with coded chroma blocks and no coded luma blocks is INTER NOMV by
/// definition, and costs nothing to say so.
/// <para/>
/// The modes themselves are coded with one Huffman code and eight different meanings for it. Six of
/// the eight schemes are fixed permutations, chosen per frame so that whichever mode the frame uses
/// most gets the one-bit code; the seventh lets the frame state its own permutation in
/// twenty-four bits; the eighth abandons the code and spends three bits per macro block. Which is
/// cheapest depends on the frame.
/// <para/>
/// Two of the modes — INTER MV LAST and INTER MV LAST2 — carry no vector and reuse the last or the
/// one before it. What counts as "last" is the last vector that pointed into the previous frame:
/// intra macro blocks and the two golden-frame modes are passed over without disturbing the pair, so
/// a run of golden-frame blocks in the middle of a pan does not reset it.
/// </remarks>
internal static class Vp3ModeReader {

  /// <summary>The coding mode of every macro block of an intra frame.</summary>
  internal const int INTRA = 1;

  private const int _INTER_MV = 2;
  private const int _INTER_MV_LAST = 3;
  private const int _INTER_MV_LAST2 = 4;
  private const int _INTER_GOLDEN_MV = 6;
  private const int _INTER_MV_FOUR = 7;

  /// <summary>How many modes there are, which is also how many entries a stated alphabet has.</summary>
  private const int _MODE_COUNT = 8;

  /// <summary>The scheme that states its own alphabet.</summary>
  private const int _SCHEME_STATED = 0;

  /// <summary>The scheme that spends three bits per macro block instead of a Huffman code.</summary>
  private const int _SCHEME_LITERAL = 7;

  internal static void ReadModes(
    Vp3BitReader reader, Vp3Geometry geometry, bool[] coded, byte[] modes, int[] alphabet) {
    var scheme = reader.ReadBits(3);

    if (scheme == _SCHEME_STATED)
      for (var mode = 0; mode < _MODE_COUNT; ++mode)
        alphabet[reader.ReadBits(3)] = mode;
    else if (scheme != _SCHEME_LITERAL)
      Vp3Tables.ModeAlphabets[scheme].CopyTo(alphabet, 0);

    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      var luma = geometry.MacroblockLumaBlocks[macroblock];
      if (!coded[luma[0]] && !coded[luma[1]] && !coded[luma[2]] && !coded[luma[3]]) {
        modes[macroblock] = 0;
        continue;
      }

      modes[macroblock] = (byte)(scheme == _SCHEME_LITERAL
        ? reader.ReadBits(3)
        : alphabet[Vp3Tables.ModeIndices.Read(reader)]);
    }
  }

  /// <summary>Marks every macro block of an intra frame, which are all coded the same way.</summary>
  internal static void AllIntra(byte[] modes, int count) {
    for (var i = 0; i < count; ++i)
      modes[i] = INTRA;
  }

  internal static void ReadMotionVectors(
    Vp3BitReader reader, Vp3Geometry geometry, bool[] coded, byte[] modes,
    sbyte[] motionX, sbyte[] motionY) {
    var lastX = 0;
    var lastY = 0;
    var previousX = 0;
    var previousY = 0;

    // Read even when nothing in the frame needs a vector.
    var literal = reader.ReadBit() != 0;

    var fourX = new int[4];
    var fourY = new int[4];

    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      var mode = modes[macroblock];
      var luma = geometry.MacroblockLumaBlocks[macroblock];
      var chroma = geometry.MacroblockChromaBlocks[macroblock];
      int x;
      int y;

      if (mode == _INTER_MV_FOUR) {
        // VP3 reads four vectors here whatever the coded flags say, and its encoder only chose this
        // mode when all four luma blocks were coded. Theora reads one per coded block instead; the
        // two agree on every stream a VP3 encoder produced, and this is the VP3 rule.
        for (var i = 0; i < 4; ++i) {
          _ReadOne(reader, literal, out fourX[i], out fourY[i]);
          motionX[luma[i]] = (sbyte)fourX[i];
          motionY[luma[i]] = (sbyte)fourY[i];
        }

        var averageX = _Average(fourX[0] + fourX[1] + fourX[2] + fourX[3]);
        var averageY = _Average(fourY[0] + fourY[1] + fourY[2] + fourY[3]);
        motionX[chroma[0]] = motionX[chroma[1]] = (sbyte)averageX;
        motionY[chroma[0]] = motionY[chroma[1]] = (sbyte)averageY;

        // The last of the four in raster order is what a later INTER MV LAST refers to.
        previousX = lastX;
        previousY = lastY;
        lastX = fourX[3];
        lastY = fourY[3];
        continue;
      }

      switch (mode) {
        case _INTER_GOLDEN_MV:
          _ReadOne(reader, literal, out x, out y);
          break;
        case _INTER_MV_LAST2:
          x = previousX;
          y = previousY;
          previousX = lastX;
          previousY = lastY;
          lastX = x;
          lastY = y;
          break;
        case _INTER_MV_LAST:
          x = lastX;
          y = lastY;
          break;
        case _INTER_MV:
          _ReadOne(reader, literal, out x, out y);
          previousX = lastX;
          previousY = lastY;
          lastX = x;
          lastY = y;
          break;
        default:
          x = 0;
          y = 0;
          break;
      }

      for (var i = 0; i < 4; ++i)
        if (coded[luma[i]]) {
          motionX[luma[i]] = (sbyte)x;
          motionY[luma[i]] = (sbyte)y;
        }

      for (var i = 0; i < 2; ++i)
        if (coded[chroma[i]]) {
          motionX[chroma[i]] = (sbyte)x;
          motionY[chroma[i]] = (sbyte)y;
        }
    }
  }

  /// <summary>
  /// Reads one motion vector, either as two Huffman codes or as two magnitudes and signs.
  /// </summary>
  /// <remarks>
  /// The literal form spends five bits on a magnitude and one on a sign, and reads the sign even when
  /// the magnitude is zero — so zero has two spellings. Theora keeps that read for compatibility with
  /// VP3, which is where it came from.
  /// </remarks>
  private static void _ReadOne(Vp3BitReader reader, bool literal, out int x, out int y) {
    if (!literal) {
      x = Vp3Tables.MotionVectorComponents.Read(reader);
      y = Vp3Tables.MotionVectorComponents.Read(reader);
      return;
    }

    x = reader.ReadBits(5);
    if (reader.ReadBit() != 0)
      x = -x;

    y = reader.ReadBits(5);
    if (reader.ReadBit() != 0)
      y = -y;
  }

  /// <summary>
  /// A quarter of a sum of four components, rounded to the nearest whole step with ties away from
  /// zero, which is the chroma vector of a macro block whose luma blocks each have their own.
  /// </summary>
  private static int _Average(int sum) {
    var magnitude = (sum < 0 ? -sum : sum) + 2 >> 2;
    return sum < 0 ? -magnitude : magnitude;
  }
}
