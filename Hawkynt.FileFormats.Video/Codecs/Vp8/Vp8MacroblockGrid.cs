using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// What was decided about every macroblock of the frame: its modes, its reference frame, its motion
/// vectors, and whether it carried any residue.
/// </summary>
/// <remarks>
/// Kept for the whole frame rather than for a row, because the loop filter needs it after every
/// macroblock has been reconstructed and it decides each macroblock's filter level from that
/// macroblock's mode and reference frame.
/// <para/>
/// There is a border of one macroblock above the first row and to the left of the first column, and
/// nothing ever writes to it. Every context in VP8 asks about the macroblock above, the one to the
/// left, and the one above and to the left; at the edges of the picture those do not exist, and RFC
/// 6386 says what to answer — no motion vector, intra-coded, subblock mode <c>B_DC_PRED</c> — which
/// is exactly what an entry that was never written holds. Spending one row and one column of
/// storage removes an edge test from every one of those lookups and, more to the point, removes the
/// chance of writing one of them wrongly.
/// </remarks>
internal sealed class Vp8MacroblockGrid {

  internal readonly int Columns;
  internal readonly int Rows;

  /// <summary>The distance between the entries of one macroblock row, which is one more than the width.</summary>
  private readonly int _stride;

  private readonly byte[] _lumaMode;
  private readonly byte[] _chromaMode;
  private readonly byte[] _referenceFrame;
  private readonly byte[] _segment;
  private readonly bool[] _skipped;
  private readonly bool[] _hasResidue;
  private readonly Vp8MotionVector[] _motionVector;

  /// <summary>Sixteen subblock modes per macroblock, meaningful when its luma mode is subblock prediction.</summary>
  private readonly byte[] _subblockModes;

  /// <summary>Sixteen subblock motion vectors per macroblock, meaningful when its luma mode is split.</summary>
  private readonly Vp8MotionVector[] _subblockMotionVectors;

  internal Vp8MacroblockGrid(int columns, int rows) {
    this.Columns = columns;
    this.Rows = rows;
    this._stride = columns + 1;

    var entries = this._stride * (rows + 1);
    this._lumaMode = new byte[entries];
    this._chromaMode = new byte[entries];
    this._referenceFrame = new byte[entries];
    this._segment = new byte[entries];
    this._skipped = new bool[entries];
    this._hasResidue = new bool[entries];
    this._motionVector = new Vp8MotionVector[entries];
    this._subblockModes = new byte[entries * 16];
    this._subblockMotionVectors = new Vp8MotionVector[entries * 16];
  }

  /// <summary>Where one macroblock's entry sits, counting the border row and column.</summary>
  internal int IndexOf(int row, int column) => (row + 1) * this._stride + column + 1;

  internal int Above(int index) => index - this._stride;

  internal int Left(int index) => index - 1;

  internal int AboveLeft(int index) => index - this._stride - 1;

  internal byte[] LumaMode => this._lumaMode;
  internal byte[] ChromaMode => this._chromaMode;
  internal byte[] ReferenceFrame => this._referenceFrame;
  internal byte[] Segment => this._segment;
  internal bool[] Skipped => this._skipped;
  internal bool[] HasResidue => this._hasResidue;
  internal Vp8MotionVector[] MotionVector => this._motionVector;

  internal Span<byte> SubblockModes(int index) => this._subblockModes.AsSpan(index * 16, 16);

  internal Span<Vp8MotionVector> SubblockMotionVectors(int index) => this._subblockMotionVectors.AsSpan(index * 16, 16);

  /// <summary>
  /// The subblock mode of a neighbour of the given macroblock, as the key frame mode context asks
  /// for it (RFC 6386, 11.3).
  /// </summary>
  /// <param name="index">The macroblock being decoded.</param>
  /// <param name="subblock">Which of its sixteen subblocks, in raster order.</param>
  internal int SubblockModeAbove(int index, int subblock) {
    if (subblock >= 4)
      return this._subblockModes[index * 16 + subblock - 4];

    var above = this.Above(index);
    var mode = this._lumaMode[above];
    return mode == Vp8Mode.SUBBLOCK_PREDICTION
      ? this._subblockModes[above * 16 + subblock + 12]
      : Vp8Mode.AsSubblockMode(mode);
  }

  /// <inheritdoc cref="SubblockModeAbove"/>
  internal int SubblockModeLeft(int index, int subblock) {
    if ((subblock & 3) != 0)
      return this._subblockModes[index * 16 + subblock - 1];

    var left = this.Left(index);
    var mode = this._lumaMode[left];
    return mode == Vp8Mode.SUBBLOCK_PREDICTION
      ? this._subblockModes[left * 16 + subblock + 3]
      : Vp8Mode.AsSubblockMode(mode);
  }

  /// <summary>
  /// The motion vector of the subblock above the given one, which may lie in the macroblock above
  /// (RFC 6386, 16.4).
  /// </summary>
  /// <remarks>
  /// A neighbour that is not split has one vector for all sixteen of its subblocks, and an
  /// intra-coded one has none — which is stored as a zero vector, because that is the answer this
  /// context wants for it.
  /// </remarks>
  internal Vp8MotionVector SubblockMotionVectorAbove(int index, int subblock) {
    if (subblock >= 4)
      return this._subblockMotionVectors[index * 16 + subblock - 4];

    var above = this.Above(index);
    return this._lumaMode[above] == Vp8Mode.SPLIT_MV
      ? this._subblockMotionVectors[above * 16 + subblock + 12]
      : this._motionVector[above];
  }

  /// <inheritdoc cref="SubblockMotionVectorAbove"/>
  internal Vp8MotionVector SubblockMotionVectorLeft(int index, int subblock) {
    if ((subblock & 3) != 0)
      return this._subblockMotionVectors[index * 16 + subblock - 1];

    var left = this.Left(index);
    return this._lumaMode[left] == Vp8Mode.SPLIT_MV
      ? this._subblockMotionVectors[left * 16 + subblock + 3]
      : this._motionVector[left];
  }
}
