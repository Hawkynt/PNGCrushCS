using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Smooths the block boundaries of a reconstructed frame (specification 8.8).
/// </summary>
internal sealed class Vp9LoopFilter {

  private readonly byte[] _levels = new byte[MAX_SEGMENTS * MAX_REF_FRAMES * MAX_MODE_LF_DELTAS];

  internal void Apply(Vp9Frame frame, Vp9ModeInfoGrid grid, Vp9FrameHeader header) {
    if (header.LoopFilterLevel == 0)
      return;

    this._BuildLevels(header);

    for (var row = 0; row < header.MiRows; row += 8)
    for (var column = 0; column < header.MiCols; column += 8)
    for (var plane = 0; plane < 3; ++plane)
    for (var pass = 0; pass < 2; ++pass)
      this._FilterSuperblock(frame, grid, header, plane, pass, row, column);
  }

  private void _BuildLevels(Vp9FrameHeader header) {
    var shift = header.LoopFilterLevel >> 5;

    for (var segment = 0; segment < MAX_SEGMENTS; ++segment) {
      var level = header.LoopFilterLevel;

      if (header.IsFeatureActive(segment, SEG_LVL_ALT_L)) {
        var data = header.Feature(segment, SEG_LVL_ALT_L);
        level = Clip3(0, MAX_LOOP_FILTER, header.SegmentationAbsoluteValues ? data : data + header.LoopFilterLevel);
      }

      if (!header.LoopFilterDeltaUpdate)
        for (var reference = 0; reference < MAX_REF_FRAMES; ++reference)
        for (var mode = 0; mode < MAX_MODE_LF_DELTAS; ++mode)
          this._levels[_LevelIndex(segment, reference, mode)] = (byte)level;

      if (!header.LoopFilterDeltaEnabled)
        continue;

      var intra = level + (header.LoopFilterReferenceDeltas[INTRA_FRAME] << shift);
      this._levels[_LevelIndex(segment, INTRA_FRAME, 0)] = (byte)Clip3(0, MAX_LOOP_FILTER, intra);

      for (var reference = LAST_FRAME; reference < MAX_REF_FRAMES; ++reference)
      for (var mode = 0; mode < MAX_MODE_LF_DELTAS; ++mode) {
        var inter = level
                    + (header.LoopFilterReferenceDeltas[reference] << shift)
                    + (header.LoopFilterModeDeltas[mode] << shift);
        this._levels[_LevelIndex(segment, reference, mode)] = (byte)Clip3(0, MAX_LOOP_FILTER, inter);
      }
    }
  }

  private static int _LevelIndex(int segment, int reference, int mode)
    => (segment * MAX_REF_FRAMES + reference) * MAX_MODE_LF_DELTAS + mode;

  private void _FilterSuperblock(
    Vp9Frame frame, Vp9ModeInfoGrid grid, Vp9FrameHeader header, int plane, int pass, int row, int column) {
    var subX = plane > 0 ? header.SubsamplingX : 0;
    var subY = plane > 0 ? header.SubsamplingY : 0;

    var dx = pass == 0 ? 1 : 0;
    var dy = pass == 0 ? 0 : 1;
    var sub = pass == 0 ? subX : subY;
    var edgeLength = pass == 0 ? 64 >> subY : 64 >> subX;

    var samples = frame.Plane(plane);
    var stride = frame.Stride(plane);
    var edges = 16 >> sub;

    for (var edge = 0; edge < edges; ++edge)
    for (var i = 0; i < edgeLength; ++i) {
      var x = pass == 0 ? column * 8 + edge * (4 << subX) : column * 8 + (i << subX);
      var y = pass == 0 ? row * 8 + (i << subY) : row * 8 + edge * (4 << subY);

      var loopColumn = ((x >> 3) >> subX) << subX;
      var loopRow = ((y >> 3) >> subY) << subY;
      if (loopRow >= grid.Rows || loopColumn >= grid.Columns)
        continue;

      var index = grid.IndexOf(loopRow, loopColumn);
      var size = grid.Sizes[index];
      var transformSize = plane > 0
        ? _ChromaTransformSize(size, grid.TransformSizes[index], subX, subY)
        : grid.TransformSizes[index];
      var superblockSize = sub == 0 ? size : Math.Max(BLOCK_16X16, (int)size);
      var skip = grid.Skips[index];
      var isIntra = grid.ReferenceFrames[index * 2] <= INTRA_FRAME;

      var isBlockEdge = pass == 0
        ? x % (8 * Vp9Tables.Blocks8x8Wide[superblockSize]) == 0
        : y % (8 * Vp9Tables.Blocks8x8High[superblockSize]) == 0;

      var isTransformEdge = _IsTransformEdge(header, pass, subX, edge, x, transformSize);
      var is32Edge = edge % 8 == 0;

      var onScreen = x < 8 * header.MiCols
                     && y < 8 * header.MiRows
                     && !(pass == 0 && x == 0)
                     && !(pass == 1 && y == 0);

      var applyFilter = onScreen && (isBlockEdge || (isTransformEdge && (isIntra || !skip)));
      if (!applyFilter)
        continue;

      var filterSize = _FilterSize(header, transformSize, is32Edge, pass, x, y, subX, subY);

      var segment = grid.SegmentIds[index];
      var reference = grid.ReferenceFrames[index * 2];
      var mode = grid.YModes[index];
      var modeType = mode is NEARESTMV or NEARMV or NEWMV ? 1 : 0;
      var level = this._levels[_LevelIndex(segment, reference, modeType)];
      if (level <= 0)
        continue;

      var shift = header.LoopFilterSharpness > 4 ? 2 : header.LoopFilterSharpness > 0 ? 1 : 0;
      var limit = header.LoopFilterSharpness > 0
        ? Clip3(1, 9 - header.LoopFilterSharpness, level >> shift)
        : Math.Max(1, level >> shift);
      var boundaryLimit = 2 * (level + 2) + limit;
      var threshold = level >> 4;

      _FilterSamples(samples, stride, x >> subX, y >> subY, dx, dy, limit, boundaryLimit, threshold, filterSize);
    }
  }

  private static int _ChromaTransformSize(int size, int transformSize, int subX, int subY)
    => size < BLOCK_8X8
      ? TX_4X4
      : Math.Min(
        transformSize,
        Vp9Tables.MaxTransformSize[Vp9Tables.SubsampledSizeLookup[(size * 2 + subX) * 2 + subY]]);

  private static bool _IsTransformEdge(Vp9FrameHeader header, int pass, int subX, int edge, int x, int transformSize) {
    if (pass == 1 && subX == 1 && (header.MiCols & 1) != 0 && (edge & 1) != 0 && x + 8 >= header.MiCols * 8)
      return false;

    return edge % (1 << transformSize) == 0;
  }

  private static int _FilterSize(
    Vp9FrameHeader header, int transformSize, bool is32Edge, int pass, int x, int y, int subX, int subY) {
    var baseSize = transformSize == TX_4X4 && is32Edge ? TX_8X8 : Math.Min(TX_16X16, transformSize);

    if (pass == 0 && subX == 1 && baseSize == TX_16X16 && x >> 3 == header.MiCols - 1)
      return TX_8X8;

    if (pass == 1 && subY == 1 && baseSize == TX_16X16 && y >> 3 == header.MiRows - 1)
      return TX_8X8;

    return baseSize;
  }

  private static void _FilterSamples(
    byte[] plane, int stride, int x, int y, int dx, int dy,
    int limit, int boundaryLimit, int threshold, int filterSize) {
    var at = y * stride + x;
    var step = dy * stride + dx;

    var q0 = plane[at];
    var q1 = plane[at + step];
    var q2 = plane[at + step * 2];
    var q3 = plane[at + step * 3];
    var p0 = plane[at - step];
    var p1 = plane[at - step * 2];
    var p2 = plane[at - step * 3];
    var p3 = plane[at - step * 4];

    var highEdgeVariance = Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;

    var filterMask = Math.Abs(p3 - p2) <= limit
                     && Math.Abs(p2 - p1) <= limit
                     && Math.Abs(p1 - p0) <= limit
                     && Math.Abs(q1 - q0) <= limit
                     && Math.Abs(q2 - q1) <= limit
                     && Math.Abs(q3 - q2) <= limit
                     && Math.Abs(p0 - q0) * 2 + Math.Abs(p1 - q1) / 2 <= boundaryLimit;

    if (!filterMask)
      return;

    var flat = filterSize >= TX_8X8
               && Math.Abs(p1 - p0) <= 1 && Math.Abs(q1 - q0) <= 1
               && Math.Abs(p2 - p0) <= 1 && Math.Abs(q2 - q0) <= 1
               && Math.Abs(p3 - p0) <= 1 && Math.Abs(q3 - q0) <= 1;

    if (filterSize == TX_4X4 || !flat) {
      _FilterNarrow(plane, at, step, highEdgeVariance);
      return;
    }

    if (filterSize == TX_8X8) {
      _FilterWide(plane, at, step, 3);
      return;
    }

    var flatter = Math.Abs(plane[at - step * 8] - p0) <= 1 && Math.Abs(plane[at + step * 7] - q0) <= 1
                  && Math.Abs(plane[at - step * 7] - p0) <= 1 && Math.Abs(plane[at + step * 6] - q0) <= 1
                  && Math.Abs(plane[at - step * 6] - p0) <= 1 && Math.Abs(plane[at + step * 5] - q0) <= 1
                  && Math.Abs(plane[at - step * 5] - p0) <= 1 && Math.Abs(plane[at + step * 4] - q0) <= 1;

    _FilterWide(plane, at, step, flatter ? 4 : 3);
  }

  private static void _FilterNarrow(byte[] plane, int at, int step, bool highEdgeVariance) {
    var q0 = plane[at] - 128;
    var q1 = plane[at + step] - 128;
    var p0 = plane[at - step] - 128;
    var p1 = plane[at - step * 2] - 128;

    var filter = highEdgeVariance ? _Clamp8(p1 - q1) : 0;
    filter = _Clamp8(filter + 3 * (q0 - p0));

    var first = _Clamp8(filter + 4) >> 3;
    var second = _Clamp8(filter + 3) >> 3;

    plane[at] = (byte)(_Clamp8(q0 - first) + 128);
    plane[at - step] = (byte)(_Clamp8(p0 + second) + 128);

    if (highEdgeVariance)
      return;

    var outer = (first + 1) >> 1;
    plane[at + step] = (byte)(_Clamp8(q1 - outer) + 128);
    plane[at - step * 2] = (byte)(_Clamp8(p1 + outer) + 128);
  }

  private static int _Clamp8(int value) => Clip3(-128, 127, value);

  private static void _FilterWide(byte[] plane, int at, int step, int sizeLog2) {
    var taps = (1 << (sizeLog2 - 1)) - 1;
    Span<int> filtered = stackalloc int[16];

    for (var i = -taps; i < taps; ++i) {
      int sum = plane[at + i * step];
      for (var j = -taps; j <= taps; ++j)
        sum += plane[at + Clip3(-(taps + 1), taps, i + j) * step];

      filtered[i + taps] = (sum + (1 << (sizeLog2 - 1))) >> sizeLog2;
    }

    for (var i = -taps; i < taps; ++i)
      plane[at + i * step] = (byte)filtered[i + taps];
  }
}
