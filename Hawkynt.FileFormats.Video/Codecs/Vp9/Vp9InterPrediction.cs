using System;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Builds a block from a reference frame, at eighth-sample accuracy and through whatever change of
/// scale lies between the two frames (specification 8.5.2).
/// </summary>
/// <remarks>
/// Two one-dimensional eight-tap convolutions, horizontal and then vertical, with the fractional part
/// of the motion vector choosing one of sixteen phases of the filter. A whole-sample vector picks the
/// phase whose only non-zero tap is 128 in the middle, so it costs the same as a copy and gives the
/// same answer as one.
/// <para/>
/// The horizontal pass produces more rows than the block has, because the vertical pass needs three
/// rows above it and four below. That intermediate is clamped back into eight bits between the two
/// passes, which matters: doing the two convolutions at full precision and rounding once would give
/// slightly different samples, and there is no "slightly" in a decoder whose output has to match
/// another decoder's byte for byte.
/// <para/>
/// Reference frames may be any size between half and sixteen times this frame's, which is why the
/// stepping through the reference is not one sample per sample. The step is a fixed-point ratio of the
/// two frames' sizes and the filter phase advances with it, so a scaled reference is resampled by the
/// same eight taps that do the sub-pixel work rather than by a separate resampler.
/// <para/>
/// Reads outside the reference are clamped to its edge rather than wrapped or refused. A motion vector
/// is allowed to point a long way outside the picture, and clamping gives the same samples an infinite
/// border of replicated edge would, for the cost of two comparisons.
/// </remarks>
internal sealed class Vp9InterPrediction {

  /// <summary>
  /// The tallest intermediate a block can need: the largest block is 64 samples high, the largest
  /// step is 80 sixteenths, and the vertical filter reaches seven rows past the last one it produces.
  /// </summary>
  private const int MAX_INTERMEDIATE_HEIGHT = (((64 - 1) * 80 + 15) >> 4) + 8;

  private const int MAX_BLOCK_WIDTH = 64;

  private readonly int[] _intermediate = new int[MAX_INTERMEDIATE_HEIGHT * MAX_BLOCK_WIDTH];
  private readonly byte[][] _predictions = [new byte[64 * 64], new byte[64 * 64]];

  /// <summary>
  /// Predicts one region of one plane, from one reference or from the average of two.
  /// </summary>
  /// <param name="destination">The plane being reconstructed.</param>
  /// <param name="destinationStride">Its row stride.</param>
  /// <param name="x">Column of the region's top left sample.</param>
  /// <param name="y">Row of the region's top left sample.</param>
  /// <param name="width">Width of the region in samples.</param>
  /// <param name="height">Height of the region in samples.</param>
  /// <param name="plane">Which plane is being predicted.</param>
  /// <param name="references">One reference frame per list, the second null when the block is not compound.</param>
  /// <param name="motionVectors">Two components per list, in eighths of a luminance sample.</param>
  /// <param name="filter">Which of the four interpolation filters to use.</param>
  /// <param name="subsamplingX">Horizontal chroma subsampling exponent for the current frame.</param>
  /// <param name="subsamplingY">Vertical chroma subsampling exponent for the current frame.</param>
  /// <param name="frameWidth">The current frame's stated width, against which the references are scaled.</param>
  /// <param name="frameHeight">The current frame's stated height.</param>
  internal void Predict(
    byte[] destination, int destinationStride, int x, int y, int width, int height, int plane,
    ReadOnlySpan<Vp9Frame?> references, ReadOnlySpan<int> motionVectors, int filter,
    int subsamplingX, int subsamplingY, int frameWidth, int frameHeight) {
    var isCompound = references[1] != null;

    for (var list = 0; list < (isCompound ? 2 : 1); ++list) {
      var reference = references[list]
        ?? throw new InvalidDataException(
          "A VP9 block predicts from a reference frame slot this stream has never written. Specification 8.2 "
          + "requires an earlier frame to have filled it.");

      _Scale(
        reference, plane, x, y, motionVectors[list * 2], motionVectors[list * 2 + 1],
        subsamplingX, subsamplingY, frameWidth, frameHeight,
        out var startX, out var startY, out var stepX, out var stepY);

      this._Convolve(reference, plane, startX, startY, stepX, stepY, width, height, filter, this._predictions[list]);
    }

    var first = this._predictions[0];
    if (!isCompound) {
      for (var row = 0; row < height; ++row)
        Array.Copy(first, row * width, destination, (y + row) * destinationStride + x, width);

      return;
    }

    var second = this._predictions[1];
    for (var row = 0; row < height; ++row) {
      var at = (y + row) * destinationStride + x;
      var from = row * width;
      for (var column = 0; column < width; ++column)
        destination[at + column] = (byte)((first[from + column] + second[from + column] + 1) >> 1);
    }
  }

  // ============================================================================================
  // Where in the reference to read (specification 8.5.2.3)
  // ============================================================================================

  private static void _Scale(
    Vp9Frame reference, int plane, int x, int y, int motionVectorRow, int motionVectorColumn,
    int subsamplingX, int subsamplingY, int frameWidth, int frameHeight,
    out int startX, out int startY, out int stepX, out int stepY) {
    if (2 * frameWidth < reference.Width || 2 * frameHeight < reference.Height
        || frameWidth > 16 * reference.Width || frameHeight > 16 * reference.Height)
      throw new InvalidDataException(
        $"This VP9 frame is {frameWidth}x{frameHeight} and predicts from a reference of "
        + $"{reference.Width}x{reference.Height}. Specification 8.5.2.3 allows a reference to be at most twice as "
        + "large and at least a sixteenth as large as the frame that uses it.");

    var xScale = ((long)reference.Width << REF_SCALE_SHIFT) / frameWidth;
    var yScale = ((long)reference.Height << REF_SCALE_SHIFT) / frameHeight;

    var baseX = (x * xScale) >> REF_SCALE_SHIFT;
    var baseY = (y * yScale) >> REF_SCALE_SHIFT;

    // The fractional part follows the luminance position even for chrominance. Profile 0 happened
    // to make both shifts one; profile 1 makes them independent, so 4:2:2, 4:4:0 and 4:4:4 must not
    // borrow the other axis' subsampling.
    var subX = plane > 0 ? subsamplingX : 0;
    var subY = plane > 0 ? subsamplingY : 0;
    var lumaX = x << subX;
    var lumaY = y << subY;
    var fractionX = ((16 * lumaX * xScale) >> REF_SCALE_SHIFT) & SUBPEL_MASK;
    var fractionY = ((16 * lumaY * yScale) >> REF_SCALE_SHIFT) & SUBPEL_MASK;

    var deltaX = ((motionVectorColumn * xScale) >> REF_SCALE_SHIFT) + fractionX;
    var deltaY = ((motionVectorRow * yScale) >> REF_SCALE_SHIFT) + fractionY;

    stepX = (int)((16 * xScale) >> REF_SCALE_SHIFT);
    stepY = (int)((16 * yScale) >> REF_SCALE_SHIFT);
    startX = (int)((baseX << SUBPEL_BITS) + deltaX);
    startY = (int)((baseY << SUBPEL_BITS) + deltaY);
  }

  // ============================================================================================
  // The two convolutions (specification 8.5.2.4)
  // ============================================================================================

  private void _Convolve(
    Vp9Frame reference, int plane, int startX, int startY, int stepX, int stepY,
    int width, int height, int filter, byte[] destination) {
    var samples = reference.Plane(plane);
    var stride = reference.Stride(plane);
    var lastColumn = reference.LastColumn(plane);
    var lastRow = reference.LastRow(plane);
    var taps = Vp9Tables.SubpelFilters;
    var intermediate = this._intermediate;

    var intermediateHeight = (((height - 1) * stepY + 15) >> 4) + 8;

    for (var row = 0; row < intermediateHeight; ++row) {
      var sourceRow = Math.Clamp((startY >> 4) + row - 3, 0, lastRow) * stride;
      var at = row * width;

      for (var column = 0; column < width; ++column) {
        var position = startX + stepX * column;
        var phase = (filter * 16 + (position & SUBPEL_MASK)) * 8;
        var whole = (position >> 4) - 3;

        var sum = 0;
        for (var tap = 0; tap < 8; ++tap)
          sum += taps[phase + tap] * samples[sourceRow + Math.Clamp(whole + tap, 0, lastColumn)];

        intermediate[at + column] = Math.Clamp((sum + 64) >> 7, 0, 255);
      }
    }

    for (var row = 0; row < height; ++row) {
      var position = (startY & SUBPEL_MASK) + stepY * row;
      var phase = (filter * 16 + (position & SUBPEL_MASK)) * 8;
      var first = (position >> 4) * width;
      var at = row * width;

      for (var column = 0; column < width; ++column) {
        var sum = 0;
        for (var tap = 0; tap < 8; ++tap)
          sum += taps[phase + tap] * intermediate[first + tap * width + column];

        destination[at + column] = (byte)Math.Clamp((sum + 64) >> 7, 0, 255);
      }
    }
  }

  // ============================================================================================
  // Which motion vector (specification 8.5.2.1 and 8.5.2.2)
  // ============================================================================================

  /// <summary>
  /// Chooses the motion vector for a block of one plane and clamps it into range.
  /// </summary>
  /// <remarks>
  /// A chrominance block of a sub-8x8 luminance block covers more than one luminance block, so it gets
  /// the average of the four rather than any one of them. The rounding is away from zero, which is why
  /// it is written out rather than left to integer division.
  /// </remarks>
  internal static void SelectAndClamp(
    int plane, int list, int blockIndex, int size, ReadOnlySpan<short> blockMotionVectors,
    int modeInfoRow, int modeInfoColumn, int modeInfoRows, int modeInfoColumns,
    int subsamplingX, int subsamplingY, Span<int> clamped) {
    int row;
    int column;

    if (plane == 0 || size >= BLOCK_8X8) {
      row = blockMotionVectors[(list * 4 + blockIndex) * 2];
      column = blockMotionVectors[(list * 4 + blockIndex) * 2 + 1];
    } else {
      var rowSum = 0;
      var columnSum = 0;
      for (var block = 0; block < 4; ++block) {
        rowSum += blockMotionVectors[(list * 4 + block) * 2];
        columnSum += blockMotionVectors[(list * 4 + block) * 2 + 1];
      }

      row = _RoundQuarter(rowSum);
      column = _RoundQuarter(columnSum);
    }

    var subX = plane == 0 ? 0 : subsamplingX;
    var subY = plane == 0 ? 0 : subsamplingY;
    var high = Vp9Tables.Blocks8x8High[size];
    var wide = Vp9Tables.Blocks8x8Wide[size];

    var toTop = -(modeInfoRow * MI_SIZE * 16) >> subY;
    var toBottom = ((modeInfoRows - high - modeInfoRow) * MI_SIZE * 16) >> subY;
    var toLeft = -(modeInfoColumn * MI_SIZE * 16) >> subX;
    var toRight = ((modeInfoColumns - wide - modeInfoColumn) * MI_SIZE * 16) >> subX;

    var spelLeft = (INTERP_EXTEND + ((wide * MI_SIZE) >> subX)) << SUBPEL_BITS;
    var spelTop = (INTERP_EXTEND + ((high * MI_SIZE) >> subY)) << SUBPEL_BITS;

    clamped[0] = Clip3(toTop - spelTop, toBottom + spelTop - SUBPEL_SHIFTS, (2 * row) >> subY);
    clamped[1] = Clip3(toLeft - spelLeft, toRight + spelLeft - SUBPEL_SHIFTS, (2 * column) >> subX);
  }

  private static int _RoundQuarter(int value) => (value < 0 ? value - 2 : value + 2) / 4;
}
