using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Reads the quantised residue of one macroblock out of a token partition and dequantises it as it
/// goes (RFC 6386, 13 and 14.1).
/// </summary>
/// <remarks>
/// Twenty-five blocks in a fixed order: the Y2 block first where there is one, then the sixteen luma
/// blocks, then four each of U and V. Each block is a run of tokens along the zig-zag scan, ending
/// at an end-of-block token or at the sixteenth position.
/// <para/>
/// The probability a token is read with depends on three things: which of the four planes the block
/// belongs to, which band the scan position falls in, and how busy the coefficients nearby are. That
/// last one is the interesting one — before the first token it counts how many of the blocks above
/// and to the left held anything, and after it, it is the size of the coefficient just decoded. The
/// meaning changes halfway through and that is deliberate: a zero before the first token suggests an
/// empty block, and a zero after one guarantees a non-empty one, because an end-of-block token
/// cannot follow a zero.
/// <para/>
/// Dequantisation happens here rather than in a pass of its own because the factor depends on the
/// scan position, which is a fact this loop has and a later pass would have to reconstruct.
/// </remarks>
internal static class Vp8TokenReader {

  /// <summary>The Y2 block's place among the twenty-five.</summary>
  internal const int Y2_BLOCK = 24;

  /// <summary>Set in the returned mask when the macroblock carried any residue at all.</summary>
  internal const int ANY_RESIDUE = 1 << 31;

  /// <summary>
  /// Reads one macroblock's residue.
  /// </summary>
  /// <param name="reader">The token partition this macroblock row is read from.</param>
  /// <param name="probabilities">The frame's token probabilities.</param>
  /// <param name="quantiser">The frame's dequantisation factors.</param>
  /// <param name="segment">Which segment this macroblock is in, which chooses among those factors.</param>
  /// <param name="hasY2">Whether the luma DC values are gathered into a Y2 block.</param>
  /// <param name="left">Nine flags saying whether the blocks to the left held anything.</param>
  /// <param name="above">Nine flags for the blocks above, this macroblock's slice of the frame-wide row.</param>
  /// <param name="coefficients">Twenty-five blocks of sixteen, in raster order within each block.</param>
  /// <returns>
  /// A bit per block, set when that block coded anything past its first position — which is what
  /// decides whether the inverse transform has to do any work — and
  /// <see cref="ANY_RESIDUE"/> when any block coded anything at all.
  /// </returns>
  internal static int ReadMacroblock(
    ref Vp8BoolDecoder reader,
    byte[] probabilities,
    Vp8Quantiser quantiser,
    int segment,
    bool hasY2,
    Span<byte> left,
    Span<byte> above,
    Span<short> coefficients) {
    coefficients.Clear();

    var mask = 0;

    if (hasY2)
      mask |= _ReadBlock(
        ref reader, probabilities, Y2_BLOCK, Vp8CoefficientPlane.Y2, 0,
        quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_Y2, 0),
        quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_Y2, 1),
        left, above, coefficients);

    var lumaPlane = hasY2 ? Vp8CoefficientPlane.LUMA_AFTER_Y2 : Vp8CoefficientPlane.LUMA_WITH_DC;
    var firstLumaCoefficient = hasY2 ? 1 : 0;
    var lumaDc = quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_LUMA, 0);
    var lumaAc = quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_LUMA, 1);
    for (var block = 0; block < 16; ++block)
      mask |= _ReadBlock(
        ref reader, probabilities, block, lumaPlane, firstLumaCoefficient,
        lumaDc, lumaAc, left, above, coefficients);

    var chromaDc = quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_CHROMA, 0);
    var chromaAc = quantiser.Factor(segment, Vp8Quantiser.BLOCK_TYPE_CHROMA, 1);
    for (var block = 16; block < 24; ++block)
      mask |= _ReadBlock(
        ref reader, probabilities, block, Vp8CoefficientPlane.CHROMA, 0,
        chromaDc, chromaAc, left, above, coefficients);

    return mask;
  }

  /// <summary>
  /// Marks a whole macroblock as empty without reading anything, which is what the skip flag means.
  /// </summary>
  /// <remarks>
  /// The neighbour contexts still have to be written, because a skipped macroblock is empty and the
  /// blocks to its right and below have to be told so. The one exception is the Y2 context of a
  /// macroblock that has no Y2 block: RFC 6386 section 13.3 says the Y2 predictor comes from the
  /// most recent macroblock that had one, so a macroblock without one leaves that flag alone rather
  /// than clearing it.
  /// </remarks>
  internal static void SkipMacroblock(Span<byte> left, Span<byte> above, bool hasY2) {
    left[..8].Clear();
    above[..8].Clear();

    if (!hasY2)
      return;

    left[8] = 0;
    above[8] = 0;
  }

  private static int _ReadBlock(
    ref Vp8BoolDecoder reader,
    byte[] probabilities,
    int block,
    int plane,
    int firstCoefficient,
    short dcFactor,
    short acFactor,
    Span<byte> left,
    Span<byte> above,
    Span<short> coefficients) {
    var leftIndex = Vp8Trees.LeftContextIndex[block];
    var aboveIndex = Vp8Trees.AboveContextIndex[block];
    var context = left[leftIndex] + above[aboveIndex];

    var target = coefficients.Slice(block * 16, 16);
    var position = firstCoefficient;
    var previousWasZero = false;

    while (position < 16) {
      var offset = Vp8Tables.CoefficientProbabilityOffset(
        plane, Vp8Trees.CoefficientBands[position], context);

      var token = reader.ReadTree(
        Vp8Trees.Token, probabilities, offset,
        previousWasZero ? Vp8Trees.TOKEN_TREE_WITHOUT_END_OF_BLOCK : 0);

      if (token == Vp8Token.END_OF_BLOCK)
        break;

      if (token == Vp8Token.ZERO) {
        context = 0;
        previousWasZero = true;
        ++position;
        continue;
      }

      int magnitude;
      if (token <= Vp8Token.FOUR)
        magnitude = token;
      else {
        var category = token - Vp8Token.CATEGORY_1;
        var extraOffset = Vp8Trees.CategoryProbabilityOffset[category];
        var extraBits = Vp8Trees.CategoryBits[category];
        magnitude = 0;
        for (var bit = 0; bit < extraBits; ++bit)
          magnitude = magnitude + magnitude + reader.ReadBool(Vp8Trees.CategoryProbabilities[extraOffset + bit]);

        magnitude += Vp8Trees.CategoryBase[category];
      }

      context = magnitude == 1 ? 1 : 2;
      previousWasZero = false;

      var value = reader.ReadFlag() != 0 ? -magnitude : magnitude;
      target[Vp8Trees.ZigZag[position]] = (short)(value * (position == 0 ? dcFactor : acFactor));
      ++position;
    }

    var carriedAnything = position != firstCoefficient;
    left[leftIndex] = above[aboveIndex] = (byte)(carriedAnything ? 1 : 0);

    return (position > 1 ? 1 << block : 0) | (carriedAnything ? ANY_RESIDUE : 0);
  }
}
