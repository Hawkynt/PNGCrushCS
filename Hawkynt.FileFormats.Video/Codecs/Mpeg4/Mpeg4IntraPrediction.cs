using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The prediction of one intra block's coefficients from its neighbours' (ISO/IEC 14496-2, 7.4.3).
/// </summary>
/// <remarks>
/// This is the largest thing MPEG-4 Part 2 adds to the block layer it inherited from H.263, and it is
/// the one a decoder can leave out and still produce a picture: without it every intra block
/// reconstructs at its own coded DC, which in a flat region is a grid of gently different greys eight
/// samples apart. That is why the arrays below span the whole picture rather than a macroblock — the
/// prediction reaches sideways and upward across macroblock boundaries, and a decoder that reset them
/// per macroblock would be wrong only at those boundaries, which is exactly where it would look like
/// something else.
/// <para/>
/// The direction is asked for separately from the prediction, and in that order, because the
/// direction also chooses which of the three scans the block's coefficients were written in. So it
/// has to be settled before a single coefficient is read, and applied after all of them have been.
/// <para/>
/// The DC is kept dequantised because the gradient that chooses the direction compares real DC
/// values; the first row and column are kept as levels and scaled by the ratio of the two blocks'
/// quantisers when they are used, which is what makes the prediction work between blocks a DQUANT
/// apart.
/// </remarks>
internal sealed class Mpeg4IntraPrediction {

  /// <summary>
  /// What a block that is not there contributes: the middle of the range, as eight-bit samples make
  /// it (ISO/IEC 14496-2, 7.4.3.1).
  /// </summary>
  /// <remarks>
  /// Two to the power of the sample depth plus two, which for eight-bit samples is 1024 — the
  /// dequantised DC of a block of mid-grey. A block outside the picture, outside the video packet, or
  /// belonging to a macroblock that was not intra coded takes this value, and no alternating current
  /// coefficients at all, so that the gradient rule has something defined to compare rather than
  /// reading whatever the arrays last held.
  /// </remarks>
  internal const int AbsentDc = 1 << 10;

  private readonly int _macroblockWidth;

  /// <summary>Each block's dequantised DC.</summary>
  private readonly int[] _dc;

  /// <summary>Each block's first row of quantised coefficients, eight per block.</summary>
  private readonly int[] _row;

  /// <summary>Each block's first column of quantised coefficients.</summary>
  private readonly int[] _column;

  /// <summary>The quantiser each block was coded with, which scales its coefficients when they are used.</summary>
  private readonly int[] _quantiser;

  /// <summary>Whether each block belongs to an intra macroblock of this video packet that has been decoded.</summary>
  private readonly bool[] _available;

  internal Mpeg4IntraPrediction(int macroblockWidth, int macroblockHeight) {
    this._macroblockWidth = macroblockWidth;
    var count = macroblockWidth * macroblockHeight * 6;
    this._dc = new int[count];
    this._row = new int[count * 8];
    this._column = new int[count * 8];
    this._quantiser = new int[count];
    this._available = new bool[count];
  }

  /// <summary>
  /// Marks every block of a macroblock as carrying nothing to predict from, which is what a
  /// macroblock that was not intra coded is.
  /// </summary>
  internal void MarkUnavailable(int address) {
    for (var block = 0; block < 6; ++block)
      this._available[address * 6 + block] = false;
  }

  /// <summary>
  /// Forgets everything before a macroblock, which is what the start of a video packet does.
  /// </summary>
  /// <remarks>
  /// A video packet is decodable on its own, so nothing inside one may predict from anything before
  /// it. Not forgetting would make a packet decode correctly only when the packet before it did,
  /// which is the whole of what video packets exist to avoid.
  /// </remarks>
  internal void BeginVideoPacket(int address) {
    Array.Clear(this._available, 0, address * 6);
  }

  /// <summary>
  /// Which way the DC gradient says this block's prediction comes from (ISO/IEC 14496-2, 7.4.3.1).
  /// </summary>
  /// <returns><c>true</c> when the prediction comes from the block above, <c>false</c> from the left.</returns>
  internal bool PredictsFromAbove(int address, int block) {
    var (left, aboveLeft, above) = this._Neighbours(address, block);

    // Whichever of the two directions the DC changes least in is the one the prediction comes from.
    // Comparing the wrong pair, or using less-than-or-equal where the standard writes less-than,
    // picks the other block wherever the two gradients are equal — which is everywhere in a flat
    // region, and is a whole picture predicted from the wrong neighbour.
    return Math.Abs(this._DcOf(left) - this._DcOf(aboveLeft)) < Math.Abs(this._DcOf(aboveLeft) - this._DcOf(above));
  }

  /// <summary>
  /// Adds the predicted coefficients into a block and records what it came to, for the blocks after it.
  /// </summary>
  /// <param name="coefficients">
  /// The block's own quantised coefficients in raster order, with the DC at position zero; the
  /// predicted values are added into it.
  /// </param>
  internal void Apply(
    int address, int block, Span<int> coefficients, int quantiser, int dcScaler, bool predictAc, bool fromAbove) {
    var (left, _, above) = this._Neighbours(address, block);
    var source = fromAbove ? above : left;

    coefficients[0] += _Divide(this._DcOf(source), dcScaler);

    if (predictAc && source >= 0 && this._available[source]) {
      var sourceQuantiser = this._quantiser[source];
      if (fromAbove) {
        for (var u = 1; u < 8; ++u)
          coefficients[u] += _Divide(this._row[source * 8 + u] * sourceQuantiser, quantiser);
      } else {
        for (var v = 1; v < 8; ++v)
          coefficients[v * 8] += _Divide(this._column[source * 8 + v] * sourceQuantiser, quantiser);
      }
    }

    var index = address * 6 + block;
    this._dc[index] = coefficients[0] * dcScaler;
    this._quantiser[index] = quantiser;
    this._available[index] = true;

    for (var i = 0; i < 8; ++i) {
      this._row[index * 8 + i] = coefficients[i];
      this._column[index * 8 + i] = coefficients[i * 8];
    }
  }

  private int _DcOf(int index) => index >= 0 && this._available[index] ? this._dc[index] : AbsentDc;

  /// <summary>
  /// Which blocks stand to the left, above and above-left of one block (ISO/IEC 14496-2, Figure 7-6).
  /// </summary>
  /// <remarks>
  /// The four luminance blocks of a macroblock are its quadrants, so two of them find their left
  /// neighbour inside the same macroblock and two find it in the macroblock before. Writing the six
  /// cases out is the only way that stays readable; deriving them from a block grid means a
  /// coordinate transform that is right for luminance and wrong for chrominance.
  /// </remarks>
  private (int Left, int AboveLeft, int Above) _Neighbours(int address, int block) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var hasLeft = column > 0;
    var hasAbove = row > 0;
    var hasAboveLeft = hasLeft && hasAbove;

    var here = address * 6;
    var toLeft = (address - 1) * 6;
    var toAbove = (address - this._macroblockWidth) * 6;
    var toAboveLeft = (address - this._macroblockWidth - 1) * 6;

    return block switch {
      0 => (hasLeft ? toLeft + 1 : -1, hasAboveLeft ? toAboveLeft + 3 : -1, hasAbove ? toAbove + 2 : -1),
      1 => (here + 0, hasAbove ? toAbove + 2 : -1, hasAbove ? toAbove + 3 : -1),
      2 => (hasLeft ? toLeft + 3 : -1, hasLeft ? toLeft + 1 : -1, here + 0),
      3 => (here + 2, here + 0, here + 1),
      _ => (hasLeft ? toLeft + block : -1, hasAboveLeft ? toAboveLeft + block : -1, hasAbove ? toAbove + block : -1),
    };
  }

  /// <summary>
  /// Integer division rounded to the nearest, away from zero at a half — the <c>//</c> of ISO/IEC
  /// 14496-2 clause 4.1.
  /// </summary>
  /// <remarks>
  /// Not truncation. The standard's operator rounds, and truncating instead loses up to half a
  /// quantiser step on every predicted DC — which in a flat region is a step of one level at every
  /// block boundary, in a picture that has nothing else in it.
  /// </remarks>
  private static int _Divide(int value, int divisor)
    => value >= 0 ? (value + (divisor >> 1)) / divisor : -((-value + (divisor >> 1)) / divisor);
}
