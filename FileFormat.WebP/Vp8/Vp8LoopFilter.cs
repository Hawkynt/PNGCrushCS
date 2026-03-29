using System;

namespace FileFormat.WebP.Vp8;

/// <summary>Simple and normal loop filters for VP8 deblocking.</summary>
internal static class Vp8LoopFilter {

  /// <summary>Apply simple loop filter to the luma plane only (adjusts p0/q0).</summary>
  public static void ApplySimple(byte[] y, int width, int height, int stride, int filterLevel, int sharpness) {
    if (filterLevel <= 0)
      return;

    var interiorLimit = _ComputeInteriorLimit(filterLevel, sharpness);
    var mbEdgeLimit = (filterLevel + 2) * 2 + interiorLimit;
    var subBlockEdgeLimit = filterLevel * 2 + interiorLimit;

    var mbCols = (width + 15) >> 4;
    var mbRows = (height + 15) >> 4;

    for (var mbRow = 0; mbRow < mbRows; ++mbRow)
      for (var mbCol = 0; mbCol < mbCols; ++mbCol) {
        var mbX = mbCol * 16;
        var mbY = mbRow * 16;

        // Vertical edges (filter along columns within the MB)
        for (var subBlock = 0; subBlock < 4; ++subBlock) {
          var x = mbX + subBlock * 4;
          if (x == 0)
            continue;

          var limit = subBlock == 0 ? mbEdgeLimit : subBlockEdgeLimit;
          for (var row = 0; row < 16; ++row) {
            var py = mbY + row;
            if (py >= height)
              break;

            var off = py * stride + x;
            _SimpleFilterHorizontalEdge(y, off, 1, limit, interiorLimit);
          }
        }

        // Horizontal edges (filter along rows within the MB)
        for (var subBlock = 0; subBlock < 4; ++subBlock) {
          var yy = mbY + subBlock * 4;
          if (yy == 0)
            continue;

          var limit = subBlock == 0 ? mbEdgeLimit : subBlockEdgeLimit;
          for (var col = 0; col < 16; ++col) {
            var px = mbX + col;
            if (px >= width)
              break;

            var off = yy * stride + px;
            _SimpleFilterVerticalEdge(y, off, stride, limit, interiorLimit);
          }
        }
      }
  }

  /// <summary>Apply normal loop filter to all planes (adjusts up to p2/q2 for strong edges).</summary>
  public static void ApplyNormal(byte[] y, int yStride, byte[] u, byte[] v, int uvStride, int width, int height, int filterLevel, int sharpness) {
    if (filterLevel <= 0)
      return;

    var interiorLimit = _ComputeInteriorLimit(filterLevel, sharpness);
    var mbEdgeLimit = (filterLevel + 2) * 2 + interiorLimit;
    var subBlockEdgeLimit = filterLevel * 2 + interiorLimit;
    var hevThreshold = filterLevel >= 40 ? 2 : filterLevel >= 15 ? 1 : 0;

    var mbCols = (width + 15) >> 4;
    var mbRows = (height + 15) >> 4;

    for (var mbRow = 0; mbRow < mbRows; ++mbRow)
      for (var mbCol = 0; mbCol < mbCols; ++mbCol) {
        var mbX = mbCol * 16;
        var mbY = mbRow * 16;

        // Luma vertical edges
        for (var subBlock = 0; subBlock < 4; ++subBlock) {
          var x = mbX + subBlock * 4;
          if (x == 0)
            continue;

          var isMbEdge = subBlock == 0;
          var limit = isMbEdge ? mbEdgeLimit : subBlockEdgeLimit;
          for (var row = 0; row < 16; ++row) {
            var py = mbY + row;
            if (py >= height)
              break;

            var off = py * yStride + x;
            if (isMbEdge)
              _NormalFilterMbEdgeH(y, off, 1, limit, interiorLimit, hevThreshold);
            else
              _NormalFilterSubBlockEdgeH(y, off, 1, limit, interiorLimit, hevThreshold);
          }
        }

        // Luma horizontal edges
        for (var subBlock = 0; subBlock < 4; ++subBlock) {
          var yy = mbY + subBlock * 4;
          if (yy == 0)
            continue;

          var isMbEdge = subBlock == 0;
          var limit = isMbEdge ? mbEdgeLimit : subBlockEdgeLimit;
          for (var col = 0; col < 16; ++col) {
            var px = mbX + col;
            if (px >= width)
              break;

            var off = yy * yStride + px;
            if (isMbEdge)
              _NormalFilterMbEdgeV(y, off, yStride, limit, interiorLimit, hevThreshold);
            else
              _NormalFilterSubBlockEdgeV(y, off, yStride, limit, interiorLimit, hevThreshold);
          }
        }

        // Chroma vertical edges (8x8 blocks, only MB edges and one sub-block edge)
        var uvMbX = mbCol * 8;
        var uvMbY = mbRow * 8;
        for (var subBlock = 0; subBlock < 2; ++subBlock) {
          var x = uvMbX + subBlock * 4;
          if (x == 0)
            continue;

          var isMbEdge = subBlock == 0;
          var limit = isMbEdge ? mbEdgeLimit : subBlockEdgeLimit;
          for (var row = 0; row < 8; ++row) {
            var py = uvMbY + row;
            if (py >= (height + 1) >> 1)
              break;

            var offU = py * uvStride + x;
            var offV = py * uvStride + x;
            if (isMbEdge) {
              _NormalFilterMbEdgeH(u, offU, 1, limit, interiorLimit, hevThreshold);
              _NormalFilterMbEdgeH(v, offV, 1, limit, interiorLimit, hevThreshold);
            } else {
              _NormalFilterSubBlockEdgeH(u, offU, 1, limit, interiorLimit, hevThreshold);
              _NormalFilterSubBlockEdgeH(v, offV, 1, limit, interiorLimit, hevThreshold);
            }
          }
        }

        // Chroma horizontal edges
        for (var subBlock = 0; subBlock < 2; ++subBlock) {
          var yy = uvMbY + subBlock * 4;
          if (yy == 0)
            continue;

          var isMbEdge = subBlock == 0;
          var limit = isMbEdge ? mbEdgeLimit : subBlockEdgeLimit;
          for (var col = 0; col < 8; ++col) {
            var px = uvMbX + col;
            if (px >= (width + 1) >> 1)
              break;

            var offU = yy * uvStride + px;
            var offV = yy * uvStride + px;
            if (isMbEdge) {
              _NormalFilterMbEdgeV(u, offU, uvStride, limit, interiorLimit, hevThreshold);
              _NormalFilterMbEdgeV(v, offV, uvStride, limit, interiorLimit, hevThreshold);
            } else {
              _NormalFilterSubBlockEdgeV(u, offU, uvStride, limit, interiorLimit, hevThreshold);
              _NormalFilterSubBlockEdgeV(v, offV, uvStride, limit, interiorLimit, hevThreshold);
            }
          }
        }
      }
  }

  #region filter core

  private static int _ComputeInteriorLimit(int filterLevel, int sharpness) {
    var limit = filterLevel;
    if (sharpness > 0) {
      limit >>= sharpness > 4 ? 2 : 1;
      if (limit > 9 - sharpness)
        limit = 9 - sharpness;
    }

    return limit < 1 ? 1 : limit;
  }

  private static int _Abs(int x) => x < 0 ? -x : x;

  private static int _Clamp127(int x) => x < -128 ? -128 : x > 127 ? 127 : x;

  private static byte _ClampByte(int x) => (byte)(x < 0 ? 0 : x > 255 ? 255 : x);

  private static bool _NeedsFilter(int p1, int p0, int q0, int q1, int limit, int interiorLimit)
    => 2 * _Abs(p0 - q0) + (_Abs(p1 - q1) >> 1) <= limit
       && _Abs(p0 - p1) <= interiorLimit
       && _Abs(q0 - q1) <= interiorLimit;

  private static bool _IsHighEdgeVariance(int p1, int p0, int q0, int q1, int hevThreshold)
    => _Abs(p1 - p0) > hevThreshold || _Abs(q1 - q0) > hevThreshold;

  // Simple filter: horizontal edge pixel filter (pixels are at offset-step and offset)
  private static void _SimpleFilterHorizontalEdge(byte[] buf, int offset, int step, int limit, int interiorLimit) {
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    var a = _Clamp127(3 * (q0 - p0));
    var a1 = (a + 4) >> 3;
    var a2 = (a + 3) >> 3;
    buf[offset - step] = _ClampByte(p0 + a2);
    buf[offset] = _ClampByte(q0 - a1);
  }

  // Simple filter: vertical edge pixel filter
  private static void _SimpleFilterVerticalEdge(byte[] buf, int offset, int step, int limit, int interiorLimit) {
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    var a = _Clamp127(3 * (q0 - p0));
    var a1 = (a + 4) >> 3;
    var a2 = (a + 3) >> 3;
    buf[offset - step] = _ClampByte(p0 + a2);
    buf[offset] = _ClampByte(q0 - a1);
  }

  // Normal filter for sub-block edges (adjusts p0/q0, optionally p1/q1)
  private static void _NormalFilterSubBlockEdgeH(byte[] buf, int offset, int step, int limit, int interiorLimit, int hevThreshold) {
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    if (_IsHighEdgeVariance(p1, p0, q0, q1, hevThreshold)) {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
    } else {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
      var a3 = (a1 + 1) >> 1;
      buf[offset - 2 * step] = _ClampByte(p1 + a3);
      buf[offset + step] = _ClampByte(q1 - a3);
    }
  }

  private static void _NormalFilterSubBlockEdgeV(byte[] buf, int offset, int step, int limit, int interiorLimit, int hevThreshold) {
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    if (_IsHighEdgeVariance(p1, p0, q0, q1, hevThreshold)) {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
    } else {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
      var a3 = (a1 + 1) >> 1;
      buf[offset - 2 * step] = _ClampByte(p1 + a3);
      buf[offset + step] = _ClampByte(q1 - a3);
    }
  }

  // Normal filter for MB edges: uses wider filter kernel (adjusts p2..q2)
  private static void _NormalFilterMbEdgeH(byte[] buf, int offset, int step, int limit, int interiorLimit, int hevThreshold) {
    var p2 = buf[offset - 3 * step];
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];
    var q2 = buf[offset + 2 * step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    if (!_NeedsMbFilter(p2, p1, p0, q0, q1, q2, interiorLimit))
      return;

    if (_IsHighEdgeVariance(p1, p0, q0, q1, hevThreshold)) {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
    } else {
      // Wide filter for MB edges
      _WideFilter(buf, offset, step);
    }
  }

  private static void _NormalFilterMbEdgeV(byte[] buf, int offset, int step, int limit, int interiorLimit, int hevThreshold) {
    var p2 = buf[offset - 3 * step];
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];
    var q2 = buf[offset + 2 * step];

    if (!_NeedsFilter(p1, p0, q0, q1, limit, interiorLimit))
      return;

    if (!_NeedsMbFilter(p2, p1, p0, q0, q1, q2, interiorLimit))
      return;

    if (_IsHighEdgeVariance(p1, p0, q0, q1, hevThreshold)) {
      var a = _Clamp127(3 * (q0 - p0));
      var a1 = (a + 4) >> 3;
      var a2 = (a + 3) >> 3;
      buf[offset - step] = _ClampByte(p0 + a2);
      buf[offset] = _ClampByte(q0 - a1);
    } else {
      _WideFilter(buf, offset, step);
    }
  }

  private static bool _NeedsMbFilter(int p2, int p1, int p0, int q0, int q1, int q2, int interiorLimit)
    => _Abs(p2 - p1) <= interiorLimit
       && _Abs(q2 - q1) <= interiorLimit;

  // Wide filter for macroblock edges: 7-tap filter modifying p2..q2
  private static void _WideFilter(byte[] buf, int offset, int step) {
    var p2 = buf[offset - 3 * step];
    var p1 = buf[offset - 2 * step];
    var p0 = buf[offset - step];
    var q0 = buf[offset];
    var q1 = buf[offset + step];
    var q2 = buf[offset + 2 * step];

    var a = _Clamp127(3 * (q0 - p0));

    // 7-tap: distribute the adjustment across 3 pixels on each side
    var a1 = (27 * a + 63) >> 7;
    var a2 = (18 * a + 63) >> 7;
    var a3 = (9 * a + 63) >> 7;

    buf[offset - step] = _ClampByte(p0 + a1);
    buf[offset] = _ClampByte(q0 - a1);
    buf[offset - 2 * step] = _ClampByte(p1 + a2);
    buf[offset + step] = _ClampByte(q1 - a2);
    buf[offset - 3 * step] = _ClampByte(p2 + a3);
    buf[offset + 2 * step] = _ClampByte(q2 - a3);
  }

  #endregion
}
