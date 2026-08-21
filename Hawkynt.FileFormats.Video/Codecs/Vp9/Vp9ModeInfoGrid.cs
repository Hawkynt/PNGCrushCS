using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// What was decided for every eight-by-eight block of the picture, and what was decided for the one
/// before it.
/// </summary>
/// <remarks>
/// VP9's prediction blocks range from 4x4 to 64x64, but everything a neighbour needs to know about a
/// block is stored at one fixed granularity: the eight-sample mode info block. A 64x64 block writes
/// the same answers into all sixty-four of the mode info positions it covers, so a neighbour never has
/// to ask how large the block it is looking at was.
/// <para/>
/// The previous frame's records are kept because an inter block may take a motion vector from the
/// block that sat in the same place a frame ago, when its spatial neighbours have nothing useful to
/// say. That is only allowed when the frame before was the same size and was shown, which is why the
/// copy is unconditional but the reading of it is not.
/// <para/>
/// Sub-block modes and motion vectors are stored per mode info position as well, four of each. Blocks
/// smaller than 8x8 are the only ones that need them, but the neighbour scan reads them from blocks of
/// every size, so every block writes them.
/// <para/>
/// The grid is whole superblocks rather than the picture's mode info extent. A 64x64 block whose top
/// left corner is the last on-screen position still writes all sixty-four of its mode info records,
/// and the loop filter still reads them before it decides they are off screen. Sizing to the picture
/// and clipping instead would make both of those a special case, for the saving of a few hundred
/// bytes.
/// </remarks>
internal sealed class Vp9ModeInfoGrid {

  /// <summary>The allocated extent, which is whole superblocks and so at least the picture's.</summary>
  internal readonly int Columns;

  internal readonly int Rows;

  internal readonly byte[] Sizes;
  internal readonly byte[] YModes;
  internal readonly byte[] SubModes;
  internal readonly byte[] TransformSizes;
  internal readonly byte[] SegmentIds;
  internal readonly byte[] InterpolationFilters;
  internal readonly bool[] Skips;
  internal readonly sbyte[] ReferenceFrames;
  internal readonly short[] MotionVectors;
  internal readonly short[] SubMotionVectors;

  internal readonly byte[] PreviousSegmentIds;
  internal readonly sbyte[] PreviousReferenceFrames;
  internal readonly short[] PreviousMotionVectors;

  internal Vp9ModeInfoGrid(int superblockColumns, int superblockRows) {
    var columns = superblockColumns * 8;
    var rows = superblockRows * 8;
    this.Columns = columns;
    this.Rows = rows;

    var count = columns * rows;
    this.Sizes = new byte[count];
    this.YModes = new byte[count];
    this.SubModes = new byte[count * 4];
    this.TransformSizes = new byte[count];
    this.SegmentIds = new byte[count];
    this.InterpolationFilters = new byte[count];
    this.Skips = new bool[count];
    this.ReferenceFrames = new sbyte[count * 2];
    this.MotionVectors = new short[count * 2 * 2];
    this.SubMotionVectors = new short[count * 2 * 4 * 2];

    this.PreviousSegmentIds = new byte[count];
    this.PreviousReferenceFrames = new sbyte[count * 2];
    this.PreviousMotionVectors = new short[count * 2 * 2];
  }

  internal int IndexOf(int row, int column) => row * this.Columns + column;

  /// <summary>Clears the segmentation map, which a change of picture size and a frame that asks to stand alone both do.</summary>
  internal void ClearSegmentMap() {
    Array.Clear(this.SegmentIds);
    Array.Clear(this.PreviousSegmentIds);
  }

  /// <summary>
  /// Keeps this frame's records for the next one to predict from (specification 8.10).
  /// </summary>
  internal void KeepForNextFrame() {
    this.ReferenceFrames.CopyTo(this.PreviousReferenceFrames, 0);
    this.MotionVectors.CopyTo(this.PreviousMotionVectors, 0);
  }

  /// <summary>Copies this frame's segment map over the persistent one (specification 8.1, step 3).</summary>
  internal void KeepSegmentMap() => this.SegmentIds.CopyTo(this.PreviousSegmentIds, 0);

  /// <summary>
  /// Writes one block's decisions into every mode info position it covers
  /// (specification 6.4.4).
  /// </summary>
  internal void Fill(
    int row, int column, int size, int yMode, int segmentId, int transformSize, bool skip, bool isInter,
    int interpolationFilter, ReadOnlySpan<sbyte> referenceFrames, ReadOnlySpan<byte> subModes,
    ReadOnlySpan<short> blockMotionVectors) {
    var high = Vp9Tables.Blocks8x8High[size];
    var wide = Vp9Tables.Blocks8x8Wide[size];

    for (var y = 0; y < high; ++y)
      for (var x = 0; x < wide; ++x) {
        var index = this.IndexOf(row + y, column + x);
        this.Sizes[index] = (byte)size;
        this.YModes[index] = (byte)yMode;
        this.SegmentIds[index] = (byte)segmentId;
        this.TransformSizes[index] = (byte)transformSize;
        this.Skips[index] = skip;
        this.ReferenceFrames[index * 2] = referenceFrames[0];
        this.ReferenceFrames[index * 2 + 1] = referenceFrames[1];

        if (isInter) {
          this.InterpolationFilters[index] = (byte)interpolationFilter;
          for (var list = 0; list < 2; ++list) {
            this.MotionVectors[(index * 2 + list) * 2] = blockMotionVectors[(list * 4 + 3) * 2];
            this.MotionVectors[(index * 2 + list) * 2 + 1] = blockMotionVectors[(list * 4 + 3) * 2 + 1];

            for (var block = 0; block < 4; ++block) {
              var at = ((index * 2 + list) * 4 + block) * 2;
              this.SubMotionVectors[at] = blockMotionVectors[(list * 4 + block) * 2];
              this.SubMotionVectors[at + 1] = blockMotionVectors[(list * 4 + block) * 2 + 1];
            }
          }
        } else {
          for (var block = 0; block < 4; ++block)
            this.SubModes[index * 4 + block] = subModes[block];
        }
      }
  }
}
