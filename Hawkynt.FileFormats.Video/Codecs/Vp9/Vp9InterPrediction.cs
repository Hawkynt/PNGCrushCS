using System;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>Builds inter-predicted VP9 blocks at eighth-sample accuracy.</summary>
internal sealed class Vp9InterPrediction {

  private const int MAX_INTERMEDIATE_HEIGHT = (((64 - 1) * 80 + 15) >> 4) + 8;
  private const int MAX_BLOCK_WIDTH = 64;

  private readonly int[] _intermediate = new int[MAX_INTERMEDIATE_HEIGHT * MAX_BLOCK_WIDTH];
  private readonly ushort[][] _predictions = [new ushort[64 * 64], new ushort[64 * 64]];

  internal void Predict(
    ushort[] destination, int destinationStride, int x, int y, int width, int height, int plane,
    ReadOnlySpan<Vp9Frame?> references, ReadOnlySpan<int> motionVectors, int filter,
    int subsamplingX, int subsamplingY, int frameWidth, int frameHeight, int bitDepth) {
    var isCompound = references[1] != null;

    for (var list = 0; list < (isCompound ? 2 : 1); ++list) {
      var reference = references[list]
        ?? throw new InvalidDataException(
          "A VP9 block predicts from a reference frame slot this stream has never written. Specification 8.2 "
          + "requires an earlier frame to have filled it.");

      if (reference.BitDepth != bitDepth)
        throw new InvalidDataException(
          $"A {bitDepth}-bit VP9 frame predicts from a {reference.BitDepth}-bit reference. Reference pictures "
          + "inside one coded sequence must use the same sample precision.");

      _Scale(
        reference, plane, x, y, motionVectors[list * 2], motionVectors[list * 2 + 1],
        subsamplingX, subsamplingY, frameWidth, frameHeight,
        out var startX, out var startY, out var stepX, out var stepY);

      this._Convolve(
        reference, plane, startX, startY, stepX, stepY, width, height, filter, bitDepth,
        this._predictions[list]);
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
        destination[at + column] = (ushort)((first[from + column] + second[from + column] + 1) >> 1);
    }
  }

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

  private void _Convolve(
    Vp9Frame reference, int plane, int startX, int startY, int stepX, int stepY,
    int width, int height, int filter, int bitDepth, ushort[] destination) {
    var samples = reference.Plane(plane);
    var stride = reference.Stride(plane);
    var lastColumn = reference.LastColumn(plane);
    var lastRow = reference.LastRow(plane);
    var taps = Vp9Tables.SubpelFilters;
    var intermediate = this._intermediate;
    var maxSample = (1 << bitDepth) - 1;

    var intermediateHeight = (((height - 1) * stepY + 15) >> 4) + 8;

    // VP9 high-bit-depth convolution uses the same filter coefficients and rounding as 8-bit. The
    // difference is the clip range after each one-dimensional pass: [0, 2^BitDepth-1], not [0,255].
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

        intermediate[at + column] = Math.Clamp((sum + 64) >> 7, 0, maxSample);
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

        destination[at + column] = (ushort)Math.Clamp((sum + 64) >> 7, 0, maxSample);
      }
    }
  }

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
