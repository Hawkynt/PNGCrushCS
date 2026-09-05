using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The filter a VarDCT frame is finished with: a weighted average of each
/// pixel's neighbourhood in which a neighbour counts for less the less it looks
/// like the pixel, so that flat areas are smoothed and edges are left alone
/// (libjxl <c>lib/jxl/render_pipeline/stage_epf.cc</c> and
/// <c>lib/jxl/epf.cc</c>).
/// </summary>
/// <remarks>
/// It runs on the three colour planes after the smoothing filter and before the
/// colour transform, in up to three passes. The first weighs twelve neighbours,
/// the other two weigh four; the first two decide how alike two pixels are over
/// a five-point cross rather than at the single pixel, and the third at the
/// pixel alone.
///
/// <para>How strongly it acts is per block and comes from two things the frame
/// already states: that block's quantisation step, and a sharpness the encoder
/// stored beside it. A block quantised finely is left almost alone; a coarse
/// one is smoothed more, which is where the blocking it would otherwise show
/// goes. A block whose strength comes out too small to matter is copied
/// through untouched.</para>
/// </remarks>
internal static class JxlEdgePreservingFilter {

  private const int _BlockDim = 8;

  /// <summary>libjxl <c>kInvSigmaNum</c>: four times root a half less one, so
  /// that a neighbour a whole sigma away counts for half.</summary>
  private const float _InvSigmaNum = -1.1715728752538099024f;

  /// <summary>libjxl <c>kMinSigma</c>, being <c>kInvSigmaNum / 0.3</c>. A block
  /// weaker than this is not worth filtering and is copied through.</summary>
  private const float _MinSigma = -3.90524291751269967465540850526868f;

  private const float _QuantMul = 0.46f;
  private const float _Pass0SigmaScale = 0.9f;
  private const float _Pass2SigmaScale = 6.5f;
  private const float _BorderSadMul = 2.0f / 3.0f;

  /// <summary>How much a difference in each plane counts towards deciding that
  /// two pixels are unalike.</summary>
  private static readonly float[] _ChannelScale = [40.0f, 5.0f, 3.5f];

  /// <summary>The twelve neighbours of the first pass, as (down, across).</summary>
  private static readonly (int Dy, int Dx)[] _WidePass = [
    (-2, 0), (-1, -1), (-1, 0), (-1, 1), (0, -2), (0, -1),
    (0, 1), (0, 2), (1, -1), (1, 0), (1, 1), (2, 0),
  ];

  /// <summary>The four neighbours of the later passes.</summary>
  private static readonly (int Dy, int Dx)[] _NarrowPass = [(-1, 0), (0, -1), (0, 1), (1, 0)];

  /// <summary>The cross two pixels are compared over.</summary>
  private static readonly (int Dy, int Dx)[] _Cross = [(0, 0), (-1, 0), (0, -1), (1, 0), (0, 1)];

  /// <summary>
  /// Filter the three planes in place.
  /// </summary>
  /// <param name="channels">The three colour planes, each width by height.</param>
  /// <param name="width">Picture width in pixels.</param>
  /// <param name="height">Picture height in pixels.</param>
  /// <param name="blockQuant">The quantisation step each block states.</param>
  /// <param name="sharpness">The sharpness each block states, 0 to 7.</param>
  /// <param name="blocksWide">Blocks across the picture.</param>
  /// <param name="blocksHigh">Blocks down it.</param>
  /// <param name="invGlobalScale">The frame's <c>inv_global_scale</c>; the
  /// quantiser's own scale is its reciprocal.</param>
  /// <param name="iterations">How many passes the frame asked for. Three runs
  /// all of them, two runs the last two, one runs only the middle one — which
  /// is libjxl's own order and not a count of the first N.</param>
  public static void Apply(
    float[][] channels,
    int width,
    int height,
    int[] blockQuant,
    int[] sharpness,
    int blocksWide,
    int blocksHigh,
    float invGlobalScale,
    int iterations
  ) {
    ArgumentNullException.ThrowIfNull(channels);
    ArgumentNullException.ThrowIfNull(blockQuant);
    ArgumentNullException.ThrowIfNull(sharpness);
    if (iterations <= 0 || channels.Length < 3)
      return;
    if (width <= 0 || height <= 0 || blocksWide <= 0 || blocksHigh <= 0)
      return;

    var invSigma = _BlockStrengths(blockQuant, sharpness, blocksWide, blocksHigh, invGlobalScale);

    if (iterations >= 3)
      _Pass(channels, width, height, invSigma, blocksWide, blocksHigh, _WidePass, _Pass0SigmaScale, overCross: true);
    if (iterations >= 1)
      _Pass(channels, width, height, invSigma, blocksWide, blocksHigh, _NarrowPass, 1.0f, overCross: true);
    if (iterations >= 2)
      _Pass(channels, width, height, invSigma, blocksWide, blocksHigh, _NarrowPass, _Pass2SigmaScale, overCross: false);
  }

  /// <summary>libjxl <c>ComputeSigma</c>: how strongly each block is filtered,
  /// kept as the reciprocal because that is how it is used.</summary>
  private static float[] _BlockStrengths(
    int[] blockQuant, int[] sharpness, int blocksWide, int blocksHigh, float invGlobalScale
  ) {
    // The quantiser states the reciprocal of what this wants.
    var quantScale = invGlobalScale > 0 ? 1.0f / invGlobalScale : 1.0f;
    var count = blocksWide * blocksHigh;
    var result = new float[count];
    for (var i = 0; i < count; ++i) {
      var quant = i < blockQuant.Length ? blockQuant[i] : 1;
      if (quant <= 0)
        quant = 1;

      var perQuant = _QuantMul / (quantScale * quant * _InvSigmaNum);
      var sharp = i < sharpness.Length ? sharpness[i] : 0;
      // Eight levels spread evenly from nothing to one.
      var sigma = perQuant * (Math.Clamp(sharp, 0, 7) / 7.0f);
      // Keep it away from zero so its reciprocal stays finite.
      result[i] = 1.0f / Math.Min(-1e-4f, sigma);
    }

    return result;
  }

  private static void _Pass(
    float[][] channels, int width, int height, float[] invSigma,
    int blocksWide, int blocksHigh, (int Dy, int Dx)[] taps, float sigmaScale, bool overCross
  ) {
    var strong = sigmaScale * 1.65f;
    var weak = strong * _BorderSadMul;

    var output = new float[3][];
    for (var c = 0; c < 3; ++c)
      output[c] = new float[width * height];

    var sads = new float[taps.Length];
    for (var y = 0; y < height; ++y) {
      var by = Math.Min(y / _BlockDim, blocksHigh - 1);
      var withinY = y % _BlockDim;
      // The first and last row of a block are weighted like a border all across.
      var rowIsBorder = withinY == 0 || withinY == _BlockDim - 1;

      for (var x = 0; x < width; ++x) {
        var bx = Math.Min(x / _BlockDim, blocksWide - 1);
        var strength = invSigma[by * blocksWide + bx];
        var at = y * width + x;

        if (strength < _MinSigma) {
          for (var c = 0; c < 3; ++c)
            output[c][at] = channels[c][at];
          continue;
        }

        var withinX = x % _BlockDim;
        var edge = rowIsBorder || withinX == 0 || withinX == _BlockDim - 1;
        var scaled = strength * (edge ? weak : strong);

        Array.Clear(sads);
        for (var c = 0; c < 3; ++c) {
          var plane = channels[c];
          var scale = _ChannelScale[c];
          for (var t = 0; t < taps.Length; ++t) {
            var (dy, dx) = taps[t];
            float difference;
            if (overCross) {
              difference = 0.0f;
              foreach (var (cy, cx) in _Cross)
                difference += Math.Abs(
                  _At(plane, width, height, x + cx, y + cy)
                  - _At(plane, width, height, x + dx + cx, y + dy + cy));
            } else {
              difference = Math.Abs(plane[at] - _At(plane, width, height, x + dx, y + dy));
            }

            sads[t] += difference * scale;
          }
        }

        var total = 1.0f;
        var sumX = channels[0][at];
        var sumY = channels[1][at];
        var sumB = channels[2][at];
        for (var t = 0; t < taps.Length; ++t) {
          var weight = 1.0f + sads[t] * scaled;
          if (weight <= 0.0f)
            continue;

          var (dy, dx) = taps[t];
          total += weight;
          sumX += weight * _At(channels[0], width, height, x + dx, y + dy);
          sumY += weight * _At(channels[1], width, height, x + dx, y + dy);
          sumB += weight * _At(channels[2], width, height, x + dx, y + dy);
        }

        output[0][at] = sumX / total;
        output[1][at] = sumY / total;
        output[2][at] = sumB / total;
      }
    }

    for (var c = 0; c < 3; ++c)
      Array.Copy(output[c], channels[c], width * height);
  }

  /// <summary>A sample, with the picture mirrored where the filter reaches past
  /// its edge.</summary>
  private static float _At(float[] plane, int width, int height, int x, int y) {
    if (x < 0)
      x = -1 - x;
    if (x >= width)
      x = 2 * width - 1 - x;
    if (y < 0)
      y = -1 - y;
    if (y >= height)
      y = 2 * height - 1 - y;

    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    return plane[y * width + x];
  }
}
