using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The sample adaptive offset filter — ITU-T H.265, clause 8.7.3.
/// </summary>
/// <remarks>
/// The loop filter H.264 did not have, and the one that does the most for how a picture looks at low
/// rates. Deblocking repairs the seams between blocks; this repairs what quantisation did
/// <em>inside</em> them, by adding a small correction the encoder measured against the original.
/// <para/>
/// Two shapes of correction, chosen per coding tree block and per plane. <b>Band offset</b> splits
/// the sample range into thirty-two bands and shifts four consecutive ones, which is what a smooth
/// gradient needs: quantisation moves a whole range of values the same way, and four bands can move
/// them back. <b>Edge offset</b> compares each sample with two of its neighbours along one of four
/// directions and corrects it by which of five shapes it sits in — a local minimum is raised, a local
/// maximum is lowered, and a sample on a slope is left alone. That is what recovers ringing around
/// edges, where quantising the transform has overshot on one side and undershot on the other.
/// <para/>
/// It reads the deblocked picture and writes a new one, because every sample's correction depends on
/// its neighbours' deblocked values. Filtering in place would feed each corrected sample into its
/// neighbour's decision and drift across the picture.
/// </remarks>
internal static class H265SampleAdaptiveOffset {

  /// <summary>Table 8-16: the two neighbours each edge class compares against.</summary>
  /// <remarks>
  /// Horizontal, vertical, and the two diagonals. One direction per coding tree block and per plane,
  /// so a block whose ringing runs one way is corrected along it and a block with none pays two bins.
  /// </remarks>
  private static readonly sbyte[] _Neighbours = [
    -1, 0, 1, 0,
    0, -1, 0, 1,
    -1, -1, 1, 1,
    1, -1, -1, 1,
  ];

  /// <summary>Applies the filter to one picture, if any of its coding tree blocks asked for it.</summary>
  internal static void Filter(H265FrameDecoder frame) {
    ArgumentNullException.ThrowIfNull(frame);

    if (!frame.Sps.SampleAdaptiveOffsetEnabled)
      return;

    var picture = frame.Picture;
    var source = new byte[][] {
      (byte[])picture.Luma.Clone(), (byte[])picture.Cb.Clone(), (byte[])picture.Cr.Clone(),
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
    H265FrameDecoder frame, int ctb, int component, byte[] source, byte[] target, int stride,
    int x0, int y0, int x1, int y1, int depth, int shift) {
    var band = frame.SaoBandOrClassAt(ctb, component);
    var bandShift = depth - 5;
    var maximum = (1 << depth) - 1;

    // Four consecutive bands, wrapping round the top of the range. A lookup of thirty-two entries
    // rather than four comparisons per sample, because this runs over every sample of the picture.
    var offsets = new int[32];
    for (var k = 0; k < 4; ++k)
      offsets[(band + k) & 31] = frame.SaoOffsetAt(ctb, component, k + 1);

    for (var y = y0; y < y1; ++y)
      for (var x = x0; x < x1; ++x) {
        if (_KeepsItsSamples(frame, x << shift, y << shift))
          continue;

        var at = y * stride + x;
        target[at] = (byte)Math.Clamp(source[at] + offsets[source[at] >> bandShift], 0, maximum);
      }
  }

  private static void _ApplyEdgeOffset(
    H265FrameDecoder frame, int ctb, int component, byte[] source, byte[] target, int stride,
    int width, int height, int x0, int y0, int x1, int y1, int depth, int shift) {
    var direction = frame.SaoBandOrClassAt(ctb, component) << 2;
    var firstX = _Neighbours[direction];
    var firstY = _Neighbours[direction + 1];
    var secondX = _Neighbours[direction + 2];
    var secondY = _Neighbours[direction + 3];

    var maximum = (1 << depth) - 1;

    // The five shapes a sample can sit in, indexed by the raw comparison result. The standard first
    // renumbers that result — 0,1,2,3,4 becomes 1,2,0,3,4 — and then looks the offset up; folding
    // the renumbering into the table once is the same thing and saves it per sample. A sample on a
    // slope takes no offset at all: it is neither a peak nor a trough and quantisation did not
    // displace it in a direction the encoder could name.
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

        // A sample whose comparison would reach outside the picture is left alone: there is nothing
        // to compare it with, and inventing a neighbour would make the correction depend on padding.
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

        target[at] = (byte)Math.Clamp(value + byShape[shape], 0, maximum);
      }
  }

  /// <summary>Whether a sample may be compared with a neighbour on the other side of a slice boundary.</summary>
  private static bool _MayReach(H265FrameDecoder frame, int x, int y, int nx, int ny) {
    var current = frame.SliceOfBlock(frame.BlockIndexAt(x, y));
    var neighbour = frame.SliceOfBlock(frame.BlockIndexAt(nx, ny));

    if (current == neighbour)
      return true;

    // Whichever of the two came second in decoding order is the one whose flag decides, because it
    // is the one that would be reaching backwards into a slice it is meant to be independent of.
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
