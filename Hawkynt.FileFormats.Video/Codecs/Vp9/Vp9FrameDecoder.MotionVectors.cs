using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Finding the motion vectors a block is likely to use, from its neighbours and from the frame before
/// it (specification 6.5).
/// </summary>
/// <remarks>
/// This is part of the syntax rather than of the decoding process, and that is not a filing decision:
/// the precision a motion vector difference is read at depends on the magnitude of the candidate it
/// is a difference from, so the candidates have to be found before the difference can be parsed.
/// <para/>
/// The search walks eight fixed neighbour positions in an order that depends on the block's shape,
/// taking the first two distinct vectors it finds. It prefers neighbours that used the same reference
/// frame; only when those run out does it take vectors that pointed at a different frame, negating
/// them if that frame lay the other way in time. Failing all of that it falls back on the vector the
/// block in the same place used a frame ago.
/// </remarks>
internal sealed partial class Vp9FrameDecoder {

  /// <summary>The two candidates the search returns, two components each (<c>RefListMv</c>).</summary>
  private readonly int[] _referenceListMotionVectors = new int[2 * 2];

  private int _referenceMotionVectorCount;

  private readonly int[] _candidateMotionVector = new int[2 * 2];
  private readonly int[] _candidateFrame = new int[2];

  /// <summary>The neighbour tally, one per reference frame, that the inter mode is read with.</summary>
  private readonly int[] _modeContext = new int[MAX_REF_FRAMES];

  private readonly int[] _sub8x8MotionVectors = new int[2 * 2];

  // ============================================================================================
  // The search (specification 6.5.1)
  // ============================================================================================

  private void _FindMotionVectorReferences(int referenceFrame, int block) {
    this._referenceMotionVectorCount = 0;
    Array.Clear(this._referenceListMotionVectors);

    var differentReferenceFound = false;
    var counter = 0;
    var search = this._miSize * MVREF_NEIGHBOURS * 2;

    // The two nearest neighbours are treated apart from the other six: they contribute to the context
    // the inter mode is read with, and they are the only ones a sub-8x8 block takes a sub-block vector
    // from rather than the whole block's.
    for (var i = 0; i < 2; ++i) {
      var candidateRow = this._miRow + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2];
      var candidateColumn = this._miCol + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2 + 1];
      if (!this._IsInside(candidateRow, candidateColumn))
        continue;

      differentReferenceFound = true;
      var index = this._grid.IndexOf(candidateRow, candidateColumn);
      counter += Vp9Tables.ModeToCounter[this._grid.YModes[index]];

      for (var list = 0; list < 2; ++list) {
        if (this._grid.ReferenceFrames[index * 2 + list] != referenceFrame)
          continue;

        this._GetSubBlockMotionVector(index, list, Vp9Tables.MotionVectorReferenceBlocks[search + i * 2 + 1], block);
        this._AddMotionVectorToList(list);
        break;
      }
    }

    for (var i = 2; i < MVREF_NEIGHBOURS; ++i) {
      var candidateRow = this._miRow + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2];
      var candidateColumn = this._miCol + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2 + 1];
      if (!this._IsInside(candidateRow, candidateColumn))
        continue;

      differentReferenceFound = true;
      this._AddIfSameReferenceFrame(candidateRow, candidateColumn, referenceFrame, false);
    }

    if (this._header.UsePreviousFrameMotionVectors)
      this._AddIfSameReferenceFrame(this._miRow, this._miCol, referenceFrame, true);

    if (differentReferenceFound)
      for (var i = 0; i < MVREF_NEIGHBOURS; ++i) {
        var candidateRow = this._miRow + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2];
        var candidateColumn = this._miCol + Vp9Tables.MotionVectorReferenceBlocks[search + i * 2 + 1];
        if (this._IsInside(candidateRow, candidateColumn))
          this._AddIfDifferentReferenceFrame(candidateRow, candidateColumn, referenceFrame, false);
      }

    if (this._header.UsePreviousFrameMotionVectors)
      this._AddIfDifferentReferenceFrame(this._miRow, this._miCol, referenceFrame, true);

    this._modeContext[referenceFrame] = Vp9Tables.CounterToContext[counter];

    for (var i = 0; i < MAX_MV_REF_CANDIDATES; ++i) {
      this._referenceListMotionVectors[i * 2] = this._ClampRow(this._referenceListMotionVectors[i * 2], MV_BORDER);
      this._referenceListMotionVectors[i * 2 + 1] =
        this._ClampColumn(this._referenceListMotionVectors[i * 2 + 1], MV_BORDER);
    }
  }

  /// <summary>
  /// Whether a candidate position may be looked at (specification 6.5.2).
  /// </summary>
  /// <remarks>
  /// The top and bottom tile edges may be crossed and the left and right ones may not. Tiles are
  /// meant to be decodable side by side, which forbids reaching sideways out of one; there is nothing
  /// to gain by forbidding it upwards, because the tile above has been decoded either way.
  /// </remarks>
  private bool _IsInside(int row, int column)
    => row >= 0 && row < this._header.MiRows && column >= this._miColStart && column < this._miColEnd;

  private void _GetSubBlockMotionVector(int index, int list, int deltaColumn, int block) {
    var which = block >= 0 ? Vp9Tables.ColumnToSubblock[block * 2 + (deltaColumn == 0 ? 1 : 0)] : 3;
    this._candidateMotionVector[list * 2] = this._grid.SubMotionVectors[((index * 2 + list) * 4 + which) * 2];
    this._candidateMotionVector[list * 2 + 1] = this._grid.SubMotionVectors[((index * 2 + list) * 4 + which) * 2 + 1];
  }

  private void _AddMotionVectorToList(int list) {
    if (this._referenceMotionVectorCount >= MAX_MV_REF_CANDIDATES)
      return;

    if (this._referenceMotionVectorCount > 0
        && this._candidateMotionVector[list * 2] == this._referenceListMotionVectors[0]
        && this._candidateMotionVector[list * 2 + 1] == this._referenceListMotionVectors[1])
      return;

    this._referenceListMotionVectors[this._referenceMotionVectorCount * 2] = this._candidateMotionVector[list * 2];
    this._referenceListMotionVectors[this._referenceMotionVectorCount * 2 + 1] =
      this._candidateMotionVector[list * 2 + 1];
    ++this._referenceMotionVectorCount;
  }

  private void _AddIfSameReferenceFrame(int row, int column, int referenceFrame, bool usePrevious) {
    for (var list = 0; list < 2; ++list) {
      this._GetBlockMotionVector(row, column, list, usePrevious);
      if (this._candidateFrame[list] != referenceFrame)
        continue;

      this._AddMotionVectorToList(list);
      return;
    }
  }

  private void _AddIfDifferentReferenceFrame(int row, int column, int referenceFrame, bool usePrevious) {
    for (var list = 0; list < 2; ++list)
      this._GetBlockMotionVector(row, column, list, usePrevious);

    var same = this._candidateMotionVector[0] == this._candidateMotionVector[2]
               && this._candidateMotionVector[1] == this._candidateMotionVector[3];

    if (this._candidateFrame[0] > INTRA_FRAME && this._candidateFrame[0] != referenceFrame) {
      this._ScaleMotionVector(0, referenceFrame);
      this._AddMotionVectorToList(0);
    }

    if (this._candidateFrame[1] > INTRA_FRAME && this._candidateFrame[1] != referenceFrame && !same) {
      this._ScaleMotionVector(1, referenceFrame);
      this._AddMotionVectorToList(1);
    }
  }

  /// <summary>
  /// Turns a candidate around when it points the other way in time (specification 6.5.9).
  /// </summary>
  private void _ScaleMotionVector(int list, int referenceFrame) {
    var candidate = this._candidateFrame[list];
    if (this._header.ReferenceFrameSignBias[candidate] == this._header.ReferenceFrameSignBias[referenceFrame])
      return;

    this._candidateMotionVector[list * 2] = -this._candidateMotionVector[list * 2];
    this._candidateMotionVector[list * 2 + 1] = -this._candidateMotionVector[list * 2 + 1];
  }

  private void _GetBlockMotionVector(int row, int column, int list, bool usePrevious) {
    var index = this._grid.IndexOf(row, column);

    if (usePrevious) {
      this._candidateMotionVector[list * 2] = this._grid.PreviousMotionVectors[(index * 2 + list) * 2];
      this._candidateMotionVector[list * 2 + 1] = this._grid.PreviousMotionVectors[(index * 2 + list) * 2 + 1];
      this._candidateFrame[list] = this._grid.PreviousReferenceFrames[index * 2 + list];
      return;
    }

    this._candidateMotionVector[list * 2] = this._grid.MotionVectors[(index * 2 + list) * 2];
    this._candidateMotionVector[list * 2 + 1] = this._grid.MotionVectors[(index * 2 + list) * 2 + 1];
    this._candidateFrame[list] = this._grid.ReferenceFrames[index * 2 + list];
  }

  // ============================================================================================
  // Clamping (specification 6.5.4 and 6.5.5)
  // ============================================================================================

  private int _ClampRow(int value, int border) {
    var high = Vp9Tables.Blocks8x8High[this._miSize];
    var toTop = -(this._miRow * MI_SIZE * 8);
    var toBottom = (this._header.MiRows - high - this._miRow) * MI_SIZE * 8;
    return Clip3(toTop - border, toBottom + border, value);
  }

  private int _ClampColumn(int value, int border) {
    var wide = Vp9Tables.Blocks8x8Wide[this._miSize];
    var toLeft = -(this._miCol * MI_SIZE * 8);
    var toRight = (this._header.MiCols - wide - this._miCol) * MI_SIZE * 8;
    return Clip3(toLeft - border, toRight + border, value);
  }

  // ============================================================================================
  // Choosing between the candidates (specification 6.5.12 and 6.5.14)
  // ============================================================================================

  /// <summary>
  /// Rounds the candidates to the precision this block will use and keeps the best two
  /// (specification 6.5.12).
  /// </summary>
  private void _FindBestReferenceMotionVectors(int list) {
    const int BORDER = (BORDERINPIXELS - INTERP_EXTEND) << 3;

    for (var i = 0; i < MAX_MV_REF_CANDIDATES; ++i) {
      var row = this._referenceListMotionVectors[i * 2];
      var column = this._referenceListMotionVectors[i * 2 + 1];

      if (!this._header.AllowHighPrecisionMotionVectors || !_UsesHighPrecision(row, column)) {
        if ((row & 1) != 0)
          row += row > 0 ? -1 : 1;

        if ((column & 1) != 0)
          column += column > 0 ? -1 : 1;
      }

      this._referenceListMotionVectors[i * 2] = this._ClampRow(row, BORDER);
      this._referenceListMotionVectors[i * 2 + 1] = this._ClampColumn(column, BORDER);
    }

    this._nearestMotionVector[list * 2] = this._referenceListMotionVectors[0];
    this._nearestMotionVector[list * 2 + 1] = this._referenceListMotionVectors[1];
    this._nearMotionVector[list * 2] = this._referenceListMotionVectors[2];
    this._nearMotionVector[list * 2 + 1] = this._referenceListMotionVectors[3];
    this._bestMotionVector[list * 2] = this._referenceListMotionVectors[0];
    this._bestMotionVector[list * 2 + 1] = this._referenceListMotionVectors[1];
  }

  /// <summary>
  /// Finds the candidates for one sub-block of a block smaller than 8x8 (specification 6.5.14).
  /// </summary>
  /// <remarks>
  /// The sub-blocks of an 8x8 region are decoded in raster order, so all but the first of them have
  /// siblings already decoded. Those siblings come first, ahead of the block's outside neighbours,
  /// because a sub-block is far more likely to move with the one beside it than with anything outside
  /// the region.
  /// </remarks>
  private void _AppendSub8x8MotionVectors(int block, int list) {
    this._FindMotionVectorReferences(this._referenceFrame[list], block);

    var found = 0;

    if (block == 0) {
      for (var i = 0; i < MAX_MV_REF_CANDIDATES; ++i) {
        this._sub8x8MotionVectors[found * 2] = this._referenceListMotionVectors[i * 2];
        this._sub8x8MotionVectors[found * 2 + 1] = this._referenceListMotionVectors[i * 2 + 1];
        ++found;
      }
    } else if (block <= 2) {
      this._sub8x8MotionVectors[0] = this._blockMotionVectors[list * 4 * 2];
      this._sub8x8MotionVectors[1] = this._blockMotionVectors[list * 4 * 2 + 1];
      found = 1;
    } else {
      this._sub8x8MotionVectors[0] = this._blockMotionVectors[(list * 4 + 2) * 2];
      this._sub8x8MotionVectors[1] = this._blockMotionVectors[(list * 4 + 2) * 2 + 1];
      found = 1;

      for (var i = 1; i >= 0 && found < 2; --i) {
        var row = this._blockMotionVectors[(list * 4 + i) * 2];
        var column = this._blockMotionVectors[(list * 4 + i) * 2 + 1];
        if (row == this._sub8x8MotionVectors[0] && column == this._sub8x8MotionVectors[1])
          continue;

        this._sub8x8MotionVectors[found * 2] = row;
        this._sub8x8MotionVectors[found * 2 + 1] = column;
        ++found;
      }
    }

    for (var i = 0; i < MAX_MV_REF_CANDIDATES && found < 2; ++i) {
      var row = this._referenceListMotionVectors[i * 2];
      var column = this._referenceListMotionVectors[i * 2 + 1];
      if (row == this._sub8x8MotionVectors[0] && column == this._sub8x8MotionVectors[1])
        continue;

      this._sub8x8MotionVectors[found * 2] = row;
      this._sub8x8MotionVectors[found * 2 + 1] = column;
      ++found;
    }

    if (found < 2) {
      this._sub8x8MotionVectors[found * 2] = 0;
      this._sub8x8MotionVectors[found * 2 + 1] = 0;
    }

    this._nearestMotionVector[list * 2] = this._sub8x8MotionVectors[0];
    this._nearestMotionVector[list * 2 + 1] = this._sub8x8MotionVectors[1];
    this._nearMotionVector[list * 2] = this._sub8x8MotionVectors[2];
    this._nearMotionVector[list * 2 + 1] = this._sub8x8MotionVectors[3];
  }
}
