using System;
using System.Collections.Generic;

namespace FileFormat.WhyPic;

/// <summary>Assembles WhyPic (.ypc) files from a <see cref="WhyPicFile"/>.</summary>
/// <remarks>
/// The encoder makes the choices WhyPic's own does at its default setting: the palette is coded
/// against its statistics when that is shorter than plain bytes, the interpolation weights are
/// fitted by expectation-maximisation until a further round saves less than two bytes, and a
/// 16-colour picture in version 1.1 has its indices renumbered by how often the prediction misses
/// each one. None of that is needed to be read back — any weights and any renumbering decode — but
/// it is what makes the file as small as WhyPic's, and in practice identical to it.
/// </remarks>
public static class WhyPicWriter {

  /// <summary>Stop fitting once a round saves fewer than this many bytes.</summary>
  private const double _CONVERGENCE_BYTES = 2.0;

  private static readonly int[] _MissDistance = [0, 6, 6, 5, 4, 3, 2, 1, 1, 2, 3, 4, 5, 6, 7, 7];
  private static readonly int[] _MissCost = [1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6, 7, 7];
  private static readonly byte[] _Placement = [7, 8, 6, 9, 5, 10, 4, 11, 3, 12, 2, 13, 1, 14, 15];

  public static byte[] ToBytes(WhyPicFile file) {
    var (width, height, colors) = (file.Width, file.Height, file.Colors);
    if (colors is not (16 or 256))
      throw new ArgumentException($"WhyPic pictures have 16 or 256 colours, not {colors}.", nameof(file));
    if (width < 1 || height < 1 || width > WhyPicFile.MaxDimension || height > WhyPicFile.MaxDimension)
      throw new ArgumentException($"WhyPic holds pictures from 1x1 to {WhyPicFile.MaxDimension}x{WhyPicFile.MaxDimension}, not {width}x{height}.", nameof(file));

    var count = width * height;
    if (file.PixelData is null || file.PixelData.Length < count)
      throw new ArgumentException($"Expected {count} pixel indices.", nameof(file));
    if (file.Palette is null || file.Palette.Length < colors * 3)
      throw new ArgumentException($"Expected a palette of {colors} entries.", nameof(file));

    var pixels = file.PixelData.AsSpan(0, count).ToArray();
    foreach (var index in pixels)
      if (index >= colors)
        throw new ArgumentException($"Palette index {index} is outside a {colors}-colour palette.", nameof(file));

    var bgr = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      bgr[i * 3] = file.Palette[i * 3 + 2];
      bgr[i * 3 + 1] = file.Palette[i * 3 + 1];
      bgr[i * 3 + 2] = file.Palette[i * 3];
    }

    var frontLoaded = file.Layout == WhyPicLayout.Version11;
    var encoder = new WhyPicRangeEncoder();
    encoder.Uniform(width, WhyPicReader.SizeScale);
    encoder.Uniform(height, WhyPicReader.SizeScale);
    if (frontLoaded) {
      encoder.Uniform(1, 2);
      encoder.Uniform(0, 2);
      encoder.Uniform(0, 2);
    } else
      encoder.Uniform(0, 2);
    encoder.Uniform(colors == 16 ? 0 : 1, 2);

    _WritePalette(encoder, bgr);

    var renumbering = frontLoaded && colors == 16;
    var (weights, renumber) = _Fit(pixels, width, colors, renumbering);

    var lambdas = new double[WhyPicContextModel.MaxOrder + 1][];
    for (var order = 0; order <= WhyPicContextModel.MaxOrder; ++order) {
      var stored = weights[order];
      for (var i = 0; i < order; ++i)
        encoder.Uniform(stored[i], WhyPicReader.WeightScale);
      lambdas[order] = new double[order + 1];
      for (var i = 0; i <= order; ++i)
        lambdas[order][i] = (double)stored[i] / WhyPicReader.WeightScale;
    }

    if (renumbering) {
      // Sent as the decoder reads it: for each new index in turn, which of the remaining old ones it is.
      Span<byte> original = stackalloc byte[16];
      for (var i = 0; i < 16; ++i)
        original[renumber![i]] = (byte)i;
      var left = new List<byte>(16);
      for (var i = 0; i < 16; ++i)
        left.Add((byte)i);
      for (var i = 0; i < 15; ++i) {
        var at = left.IndexOf(original[i]);
        encoder.Uniform(at, 16 - i);
        left.RemoveAt(at);
      }

      for (var i = 0; i < count; ++i)
        pixels[i] = renumber![pixels[i]];
    }

    _EncodePixels(encoder, pixels, width, colors, frontLoaded, lambdas);

    var code = encoder.Finish();
    var output = new byte[2 + code.Length];
    output[0] = (byte)'Y';
    output[1] = (byte)'p';
    code.CopyTo(output, 2);
    return output;
  }

  private static void _WritePalette(WhyPicRangeEncoder encoder, byte[] bgr) {
    var n = bgr.Length;
    var seenShare = new double[n];
    Span<int> counts = stackalloc int[256];
    counts.Clear();
    for (var i = 0; i < n; ++i) {
      if (i > 0)
        seenShare[i] = (double)counts[bgr[i]] / i;
      ++counts[bgr[i]];
    }

    // The weight of the even split against the running counts, fitted the same way as the pixels'.
    var previous = 0.5;
    double lambda;
    for (var round = 0; ; ++round) {
      lambda = 0;
      for (var i = 1; i < n; ++i)
        lambda += previous * 1 / 256 / (previous * 1 / 256 + (-previous + 1) * seenShare[i]);
      lambda /= n - 1;
      if ((lambda - previous < 0.0001 && lambda - previous > -0.0001) || round > 100000)
        break;
      previous = lambda;
    }

    var stored = (int)(lambda * WhyPicReader.PaletteWeightScale);
    lambda = (double)stored / WhyPicReader.PaletteWeightScale;
    var bits = -Math.Log(1.0 / 256);
    for (var i = 1; i < n; ++i)
      bits += -Math.Log(lambda * 1 / 256 + (-lambda + 1) * seenShare[i]);
    bits /= Math.Log(2);

    var plainBits = -Math.Log(1.0 / 256) / Math.Log(2);
    var weightBits = -Math.Log(1.0 / WhyPicReader.PaletteWeightScale) / Math.Log(2);

    if (bits + weightBits < plainBits * 3 * (n / 3) && stored is > 0 and < WhyPicReader.PaletteWeightScale) {
      var intervals = new (int Low, int High)[n];
      counts.Clear();
      var usable = true;
      for (var i = 0; i < n && usable; ++i) {
        var below = 0;
        for (var v = 0; v < bgr[i]; ++v)
          below += counts[v];
        intervals[i] = WhyPicPaletteModel.Interval(lambda, i, bgr[i], below, counts[bgr[i]]);
        usable = intervals[i].Low < intervals[i].High;
        ++counts[bgr[i]];
      }

      if (usable) {
        encoder.Uniform(0, 2);
        encoder.Uniform(stored, WhyPicReader.PaletteWeightScale);
        foreach (var (low, high) in intervals)
          encoder.Encode(low, high, WhyPicContextModel.ProbabilityScale);
        return;
      }
    }

    encoder.Uniform(1, 2);
    foreach (var value in bgr)
      encoder.Uniform(value, 256);
  }

  /// <summary>
  /// Runs the model over the picture once to learn how well each order predicts it, fits the
  /// interpolation weights to that, and chooses the 16-colour renumbering.
  /// </summary>
  private static (int[][] Weights, byte[]? Renumber) _Fit(byte[] pixels, int width, int colors, bool renumbering) {
    var count = pixels.Length;
    var model = new WhyPicContextModel(colors);
    var seen = new byte[count];
    Span<byte> neighbours = stackalloc byte[8];

    // For each order, the probability every model of that order gave the true colour, in picture order.
    var shares = new List<float>[WhyPicContextModel.MaxOrder + 1];
    for (var g = 0; g <= WhyPicContextModel.MaxOrder; ++g)
      shares[g] = [];

    var samples = new int[WhyPicContextModel.MaxOrder + 1];
    var missed = new int[16];
    var missWeight = new int[16];

    for (var at = 0; at < count; ++at) {
      WhyPicNeighbourhood.Gather(seen, at, width, neighbours);
      var symbol = pixels[at];
      if (at == 0)
        neighbours.Fill(symbol);

      model.Lookup(neighbours);
      var order = model.Order;
      ++samples[order];
      var list = shares[order];
      for (var g = 0; g < order; ++g)
        list.Add((float)model.Frequencies[g][symbol] / model.Totals[g]);

      if (renumbering) {
        var row = order > 0 ? model.Frequencies[order - 1] : null;
        var max = 0;
        if (row != null)
          for (var c = 1; c < 16; ++c)
            if (row[c] > row[max])
              max = c;
        if (max != symbol) {
          ++missed[symbol];
          missWeight[symbol] += _MissDistance[max];
        }
      }

      seen[at] = symbol;
      model.Update(neighbours, symbol);
    }

    var weights = _FitWeights(shares, samples, 1.0 / colors);
    return (weights, renumbering ? _Renumbering(missed, missWeight) : null);
  }

  private static int[][] _FitWeights(List<float>[] shares, int[] sampleCounts, double uniform) {
    var weights = new int[WhyPicContextModel.MaxOrder + 1][];
    var terms = new double[WhyPicContextModel.MaxOrder + 1];
    var next = new double[WhyPicContextModel.MaxOrder + 1];

    for (var g = 0; g <= WhyPicContextModel.MaxOrder; ++g) {
      var lambda = new double[g + 1];
      for (var i = 0; i <= g; ++i)
        lambda[i] = 1.0 / (g + 1);

      var list = shares[g];
      var samples = sampleCounts[g];

      var previousBits = (double)int.MaxValue;
      while (true) {
        Array.Clear(next);
        var bits = 0.0;
        for (var s = 0; s < samples; ++s) {
          terms[0] = lambda[0] * uniform;
          for (var j = 1; j <= g; ++j)
            terms[j] = lambda[j] * list[s * g + j - 1];

          var total = 0.0;
          for (var j = 0; j <= g; ++j)
            total += terms[j];
          for (var j = 0; j <= g; ++j)
            next[j] += terms[j] / total;
          bits += -Math.Log(total);
        }

        if (samples > 0) {
          for (var j = 0; j <= g; ++j)
            next[j] /= samples;

          // Rounding leaves the weights a hair off summing to one; the difference is shared out.
          var rest = 1.0;
          for (var j = 0; j <= g; ++j)
            rest -= next[j];
          for (var j = 1; j <= g; ++j)
            next[j] += rest / g;
          for (var j = 1; j <= g; ++j)
            if (next[j] < 0)
              next[j] = 0;
        }

        for (var j = 0; j <= g; ++j)
          lambda[j] = next[j];

        bits /= Math.Log(2);
        if (previousBits - bits < _CONVERGENCE_BYTES * 8)
          break;
        previousBits = bits;
      }

      weights[g] = _Quantise(lambda, g);
    }

    return weights;
  }

  /// <summary>Puts the weights on the stored scale, keeping the even split above zero so no colour is ever impossible.</summary>
  private static int[] _Quantise(double[] lambda, int g) {
    var stored = new int[g + 1];
    var sum = 0;
    for (var i = 0; i < g; ++i) {
      var value = (int)(lambda[i] * WhyPicReader.WeightScale);
      if (value < 0)
        value = 0;
      if (i == 0 && value == 0)
        value = 1;
      if (value >= WhyPicReader.WeightScale)
        value = WhyPicReader.WeightScale - 1;
      stored[i] = value;
      sum += value;
    }

    stored[g] = WhyPicReader.WeightScale - sum;

    // Truncation can only lose weight, but the clamps above can add it; what overshoots is taken
    // from the heaviest context model, as WhyPic does.
    while (stored[g] < 0) {
      var largest = 0;
      for (var i = 1; i < g; ++i)
        if (stored[i] > stored[largest])
          largest = i;
      var take = Math.Min(-stored[g], stored[largest] - (largest == 0 ? 1 : 0));
      stored[largest] -= take;
      stored[g] += take;
      sum -= take;
    }

    return stored;
  }

  /// <summary>Chooses which colour goes to index 0 and orders the rest by how often they were mispredicted.</summary>
  private static byte[] _Renumbering(int[] missed, int[] missWeight) {
    Span<int> sorted = stackalloc int[15];
    Span<int> origin = stackalloc int[15];
    var bestCost = int.MaxValue;
    var front = 0;

    for (var i = 0; i < 16; ++i) {
      var k = 0;
      for (var j = 0; j < 16; ++j)
        if (j != i)
          sorted[k++] = missed[j];
      _SortDescending(sorted, null);

      var cost = missWeight[i];
      for (var j = 0; j < 15; ++j)
        cost += sorted[j] * _MissCost[j];
      if (cost < bestCost) {
        bestCost = cost;
        front = i;
      }
    }

    var n = 0;
    for (var i = 0; i < 16; ++i)
      if (i != front) {
        sorted[n] = missed[i];
        origin[n] = i;
        ++n;
      }
    _SortDescending(sorted, origin);

    var renumber = new byte[16];
    renumber[front] = 0;
    for (var i = 0; i < 15; ++i)
      renumber[origin[i]] = _Placement[i];
    return renumber;
  }

  // An exchange sort that only swaps strictly smaller ahead of larger, so equal counts keep their order.
  private static void _SortDescending(Span<int> keys, Span<int> companions) {
    for (var pass = 0; pass < keys.Length; ++pass)
      for (var k = 0; k + 1 < keys.Length; ++k)
        if (keys[k] < keys[k + 1]) {
          (keys[k], keys[k + 1]) = (keys[k + 1], keys[k]);
          if (!companions.IsEmpty)
            (companions[k], companions[k + 1]) = (companions[k + 1], companions[k]);
        }
  }

  private static void _EncodePixels(WhyPicRangeEncoder encoder, byte[] pixels, int width, int colors, bool frontLoaded, double[][] weights) {
    var count = pixels.Length;
    var model = new WhyPicContextModel(colors);
    var coded = new byte[count];
    var uniform = 1.0 / colors;
    Span<byte> neighbours = stackalloc byte[8];

    for (var at = 0; at < count; ++at) {
      WhyPicNeighbourhood.Gather(coded, at, width, neighbours);
      model.Lookup(neighbours);
      var lambda = weights[model.Order];
      var value = pixels[at];
      var symbol = (int)value;

      if (frontLoaded) {
        var max = model.MostLikely();
        model.SwapToFront(max);
        symbol = symbol == max ? 0 : symbol == 0 ? max : symbol;
      }

      model.Accumulate();
      var low = model.Cumulative(lambda, symbol, uniform);
      var high = model.Cumulative(lambda, symbol + 1, uniform);
      encoder.Encode(low, high, WhyPicContextModel.ProbabilityScale);

      coded[at] = value;
      if (at == 0)
        neighbours.Fill(value);
      model.Update(neighbours, value);
    }
  }
}
