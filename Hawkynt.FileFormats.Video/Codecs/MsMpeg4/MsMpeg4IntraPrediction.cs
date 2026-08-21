using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The prediction of one intra block's coefficients from its neighbours', as Microsoft's MPEG-4
/// version 2 states it.
/// </summary>
/// <remarks>
/// Nearly ISO/IEC 14496-2 clause 7.4.3, and the differences are small enough to be worth writing down
/// one at a time, because each of them is invisible until it is not:
/// <list type="bullet">
/// <item><b>The gradient test uses <c>&lt;=</c> where the standard uses <c>&lt;</c>.</b> The two
/// disagree only where the two gradients are exactly equal, which is everywhere in a flat region — so
/// the wrong one of them predicts a whole flat picture from the wrong neighbour.</item>
/// <item><b>The DC compared is the quantised one and the absent value is 128, not 1024.</b> Version 2
/// quantises the DC with a step of eight whatever the picture's quantiser is, so the two differ by a
/// constant factor and the comparison could be made either way; keeping it quantised is what the
/// format's own description does and is one multiplication fewer.</item>
/// <item><b>A block of a macroblock that was not intra coded is absent, not merely unavailable.</b> In
/// a predicted picture most macroblocks are not intra, so this is the common case rather than an edge
/// one.</item>
/// <item><b>Nothing crosses a slice boundary.</b> A slice is a run of whole macroblock rows and is
/// meant to be decodable on its own, so the row above the first row of a slice counts as absent even
/// though it is inside the picture.</item>
/// <item><b>The alternating current predictors are not rescaled.</b> The standard scales them by the
/// ratio of the two blocks' quantisers, because a macroblock there may change the quantiser; version 2
/// states the quantiser once per picture and gives the macroblock layer no way to change it, so the
/// ratio is always one and the multiplication and its rounding would be arithmetic that cannot alter a
/// result.</item>
/// </list>
/// </remarks>
internal sealed class MsMpeg4IntraPrediction {

  /// <summary>
  /// What a block that is not there contributes, in the quantised units the DC is compared in.
  /// </summary>
  /// <remarks>
  /// Mid-grey: 1024 over the DC step of eight, rounded, which is 128. The format's own description
  /// writes it as <c>(1024 + dc_scale/2) / dc_scale</c>, and version 2 fixes <c>dc_scale</c> at eight.
  /// </remarks>
  internal const int AbsentDc = 128;

  private readonly int _macroblockWidth;
  private readonly int _sliceHeight;

  /// <summary>Each block's quantised DC.</summary>
  private readonly int[] _dc;

  /// <summary>Each block's first row of quantised coefficients, eight per block.</summary>
  private readonly int[] _row;

  /// <summary>Each block's first column.</summary>
  private readonly int[] _column;

  /// <summary>Whether each block belongs to an intra macroblock that has been decoded.</summary>
  private readonly bool[] _available;

  internal MsMpeg4IntraPrediction(int macroblockWidth, int macroblockHeight, int sliceHeight) {
    this._macroblockWidth = macroblockWidth;
    this._sliceHeight = sliceHeight < 1 ? macroblockHeight : sliceHeight;
    var count = macroblockWidth * macroblockHeight * 6;
    this._dc = new int[count];
    this._row = new int[count * 8];
    this._column = new int[count * 8];
    this._available = new bool[count];
  }

  /// <summary>Marks every block of a macroblock as carrying nothing to predict from.</summary>
  /// <remarks>
  /// Which is what a macroblock that was predicted rather than intra coded is, and what a skipped one
  /// is. Both are the ordinary case in a predicted picture.
  /// </remarks>
  internal void MarkUnavailable(int address) {
    for (var block = 0; block < 6; ++block)
      this._available[address * 6 + block] = false;
  }

  /// <summary>Which way the DC gradient says this block's prediction comes from.</summary>
  /// <returns><c>true</c> when the prediction comes from the block above, <c>false</c> from the left.</returns>
  internal bool PredictsFromAbove(int address, int block) {
    var (left, aboveLeft, above) = this._Neighbours(address, block);

    // Less-than-or-equal, and this is the whole of the difference from ISO/IEC 14496-2 7.4.3.1.
    return Math.Abs(this._DcOf(aboveLeft) - this._DcOf(left)) <= Math.Abs(this._DcOf(aboveLeft) - this._DcOf(above));
  }

  /// <summary>
  /// Adds the predicted coefficients into a block and records what it came to, for the blocks after it.
  /// </summary>
  /// <param name="coefficients">
  /// The block's quantised coefficients in raster order with the DC at position zero; the predictions
  /// are added into it.
  /// </param>
  internal void Apply(int address, int block, Span<int> coefficients, bool predictAc, bool fromAbove) {
    var (left, _, above) = this._Neighbours(address, block);
    var source = fromAbove ? above : left;

    coefficients[0] += this._DcOf(source);

    if (predictAc && source >= 0 && this._available[source])
      if (fromAbove) {
        for (var u = 1; u < 8; ++u)
          coefficients[u] += this._row[source * 8 + u];
      } else {
        for (var v = 1; v < 8; ++v)
          coefficients[v * 8] += this._column[source * 8 + v];
      }

    var index = address * 6 + block;
    this._dc[index] = coefficients[0];
    this._available[index] = true;

    for (var i = 0; i < 8; ++i) {
      this._row[index * 8 + i] = coefficients[i];
      this._column[index * 8 + i] = coefficients[i * 8];
    }
  }

  private int _DcOf(int index) => index >= 0 && this._available[index] ? this._dc[index] : AbsentDc;

  /// <summary>Whether two macroblocks are in the same slice, which is what prediction may not cross.</summary>
  private bool _SameSlice(int address, int other)
    => address / this._macroblockWidth / this._sliceHeight == other / this._macroblockWidth / this._sliceHeight;

  /// <summary>
  /// Which blocks stand to the left, above and above-left of one block (ISO/IEC 14496-2, Figure 7-6).
  /// </summary>
  /// <remarks>
  /// The four luminance blocks of a macroblock are its quadrants, so two of them find their left
  /// neighbour inside the same macroblock and two find it in the macroblock before. The six cases are
  /// written out because deriving them from a block grid needs a coordinate transform that is right
  /// for luminance and wrong for chrominance.
  /// </remarks>
  private (int Left, int AboveLeft, int Above) _Neighbours(int address, int block) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var toLeft = address - 1;
    var toAbove = address - this._macroblockWidth;
    var toAboveLeft = toAbove - 1;

    var hasLeft = column > 0;
    var hasAbove = row > 0 && this._SameSlice(address, toAbove);
    var hasAboveLeft = hasLeft && hasAbove;

    var here = address * 6;
    var l = toLeft * 6;
    var a = toAbove * 6;
    var al = toAboveLeft * 6;

    return block switch {
      0 => (hasLeft ? l + 1 : -1, hasAboveLeft ? al + 3 : -1, hasAbove ? a + 2 : -1),
      1 => (here + 0, hasAbove ? a + 2 : -1, hasAbove ? a + 3 : -1),
      2 => (hasLeft ? l + 3 : -1, hasLeft ? l + 1 : -1, here + 0),
      3 => (here + 2, here + 0, here + 1),
      _ => (hasLeft ? l + block : -1, hasAboveLeft ? al + block : -1, hasAbove ? a + block : -1),
    };
  }
}
