using System;

namespace FileFormat.Codecs.H265;

/// <summary>The sample adaptive offset filter — ITU-T H.265, clause 8.7.3.</summary>
internal static class H265SampleAdaptiveOffset {

  private static readonly sbyte[] _Neighbours = [
    -1, 0, 1, 0,
    0, -1, 0, 1,
    -1, -1, 1, 1,
    1, -1, -1, 1,
  ];

  internal static void Filter(H265FrameDecoder frame) {
    ArgumentNullException.ThrowIfNull(frame);

    if (!frame.Sps.SampleAdaptiveOffsetEnabled)
      return;

    var picture = frame.Picture;
    var source = new ushort[][] {
      (ushort[])picture.Luma.Clone(), (ushort[])picture.Cb.Clone(), (ushort[])picture.Cr.Clone(),
    };

    var log2Ctb = frame.Sps.CtbLog2SizeY;
    var across = frame.Sps.PicWidthInCtbsY;

    for (var ctb = 0; ctb < frame.Sps.PicSizeInCtbsY; ++ctb) {
      var ctbX = (ctb % across) << log2Ctb;
      var ctbY = (ctb / across) << log2Ctb;

      for (var component = 0; component < 3; ++component) {
        var type = frame.SaoTypeAt(ctb, component);
        if (type == 0)
          continue;

        var shift = component == 0 ? 0 : 1;
        var target = component == 0 ? picture.Luma : picture.Chroma(component - 1);
        var stride = component == 0 ? picture.Width : picture.ChromaWidth;
        var width = stride;
        var height = component == 0 ? picture.Height : picture.ChromaHeight;
        var depth = component == 0 ? frame.Sps.BitDepthLuma : frame.Sps.BitDepthChroma;

        var x0 = ctbX >> shift;
        var y0 = ctbY >> shift;
        var x1 = Math.Min(x0 + (1 << (log2Ctb - shift)), width);
        var y1 = Math.Min(y0 + (1 << (log2Ctb - shift)), height);

        if (type == 1)
          _ApplyBandOffset(frame, ctb, component, source[component], target, stride, x0, y0, x1, y1, depth, shift);
        else
          _ApplyEdgeOffset(
            frame, ctb, component, source[component], target, stride, width, height,
            x0, y0, x1, y1, depth, shift);
      }
    }
  }

  private static void _ApplyBandOffset(
    H265FrameDecoder frame, int ctb, int component, ushort[] source, ushort[] target, int stride,
    int x0, int y0, int x1, int y1, int depth, int shift) {
    var band = frame.SaoBandOrClassAt(ctb, component);
    var bandShift = depth - 5;
    var maximum = (1 << depth) - 1;

    var offsets = new int[32];
    for (var k = 0; k < 4; ++k)
      offsets[(band + k) & 31] = frame.SaoOffsetAt(ctb, component, k + 1);

    for (var y = y0; y < y1; ++y)
      for (var x = x0; x < x1; ++x) {
        if (_KeepsItsSamples(frame, x << shift, y << shift))
          continue;

        var at = y * stride + x;
        target[at] = (ushort)Math.Clamp(source[at] + offsets[source[at] >> bandShift], 0, maximum);
      }
  }

  private static void _ApplyEdgeOffset(
    H265FrameDecoder frame, int ctb, int component, ushort[] source, ushort[] target, int stride,
    int width, int height, int x0, int y0, int x1, int y1, int depth, int shift) {
    var direction = frame.SaoBandOrClassAt(ctb, component) << 2;
    var firstX = _Neighbours[direction];
    var firstY = _Neighbours[direction + 1];
    var secondX = _Neighbours[direction + 2];
    var secondY = _Neighbours[direction + 3];

    var maximum = (1 << depth) - 1;
    Span<int> byShape = stackalloc int[5];
    byShape[0] = frame.SaoOffsetAt(ctb, component, 1);
    byShape[1] = frame.SaoOffsetAt(ctb, component, 2);
    byShape[2] = 0;
    byShape[3] = frame.SaoOffsetAt(ctb, component, 3);
    byShape[4] = frame.SaoOffsetAt(ctb, component, 4);

    for (var y = y0; y < y1; ++y)
      for (var x = x0; x < x1; ++x) {
        var nx0 = x + firstX;
        var ny0 = y + firstY;
        var nx1 = x + secondX;
        var ny1 = y + secondY;

        if (nx0 < 0 || ny0 < 0 || nx0 >= width || ny0 >= height
            || nx1 < 0 || ny1 < 0 || nx1 >= width || ny1 >= height)
          continue;

        if (_KeepsItsSamples(frame, x << shift, y << shift))
          continue;

        if (!_MayReach(frame, x << shift, y << shift, nx0 << shift, ny0 << shift)
            || !_MayReach(frame, x << shift, y << shift, nx1 << shift, ny1 << shift))
          continue;

        var at = y * stride + x;
        var value = source[at];
        var shape = 2 + Math.Sign(value - source[ny0 * stride + nx0]) + Math.Sign(value - source[ny1 * stride + nx1]);

        target[at] = (ushort)Math.Clamp(value + byShape[shape], 0, maximum);
      }
  }

  private static bool _MayReach(H265FrameDecoder frame, int x, int y, int nx, int ny) {
    // Tile prediction is always independent, but in-loop filtering may cross a tile edge when the
    // PPS explicitly permits it. Keep this decision separate from IsAvailableAt(), whose tile rule
    // is intentionally stricter for prediction and CABAC-neighbour derivation.
    if (!frame.SameTileAt(x, y, nx, ny) && !frame.Pps.LoopFilterAcrossTilesEnabled)
      return false;

    var current = frame.SliceOfBlock(frame.BlockIndexAt(x, y));
    var neighbour = frame.SliceOfBlock(frame.BlockIndexAt(nx, ny));

    if (current == neighbour)
      return true;

    // IsAvailableAt deliberately rejects cross-tile neighbours, so when the PPS permits filtering
    // across tiles determine decoding order from the recorded slice identities instead. For a real
    // slice boundary the later slice's flag decides; slice ordinals are assigned in decoding order.
    if (!frame.SameTileAt(x, y, nx, ny))
      return current > neighbour
        ? frame.LoopFilterAcrossSlices(current)
        : frame.LoopFilterAcrossSlices(neighbour);

    return frame.IsAvailableAt(x, y, nx, ny)
      ? frame.LoopFilterAcrossSlices(current)
      : frame.LoopFilterAcrossSlices(neighbour);
  }

  private static bool _KeepsItsSamples(H265FrameDecoder frame, int x, int y) {
    var block = frame.BlockIndexAt(x, y);
    return frame.IsTransquantBypassAt(block)
           || (frame.Sps.PcmLoopFilterDisabled && frame.IsPulseCodeModulatedAt(block));
  }
}
