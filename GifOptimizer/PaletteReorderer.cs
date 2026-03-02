using System;
using System.Drawing;
using System.Linq;

namespace GifOptimizer;

internal static class PaletteReorderer {
  public static (Color[] newPalette, byte[] remapTable) Reorder(Color[] palette, byte[] pixels,
    PaletteReorderStrategy strategy) {
    return strategy switch {
      PaletteReorderStrategy.Original => (palette, _IdentityRemap(palette.Length)),
      PaletteReorderStrategy.FrequencySorted => _FrequencySort(palette, pixels),
      PaletteReorderStrategy.LuminanceSorted => _LuminanceSort(palette),
      PaletteReorderStrategy.SpatialLocality => _SpatialLocalitySort(palette, pixels),
      PaletteReorderStrategy.LzwRunAware => _LzwRunAwareSort(palette, pixels),
      PaletteReorderStrategy.HilbertCurve => _HilbertCurveSort(palette),
      PaletteReorderStrategy.CompressionOptimized => _CompressionOptimizedSort(palette, pixels),
      _ => (palette, _IdentityRemap(palette.Length))
    };
  }

  public static byte[] ApplyRemap(byte[] pixels, byte[] remapTable) {
    var result = new byte[pixels.Length];
    for (var i = 0; i < pixels.Length; ++i)
      result[i] = remapTable[pixels[i]];
    return result;
  }

  private static byte[] _IdentityRemap(int size) {
    var remap = new byte[size];
    for (var i = 0; i < size; ++i)
      remap[i] = (byte)i;
    return remap;
  }

  private static (Color[] newPalette, byte[] remapTable) _FrequencySort(Color[] palette, byte[] pixels) {
    var freq = new int[palette.Length];
    foreach (var p in pixels)
      if (p < freq.Length)
        ++freq[p];

    var indices = Enumerable.Range(0, palette.Length).ToArray();
    Array.Sort(indices, (a, b) => freq[b].CompareTo(freq[a]));

    return _BuildRemappedResult(palette, indices);
  }

  private static (Color[] newPalette, byte[] remapTable) _LuminanceSort(Color[] palette) {
    var indices = Enumerable.Range(0, palette.Length).ToArray();
    Array.Sort(indices, (a, b) => {
      var lumA = 0.299 * palette[a].R + 0.587 * palette[a].G + 0.114 * palette[a].B;
      var lumB = 0.299 * palette[b].R + 0.587 * palette[b].G + 0.114 * palette[b].B;
      return lumA.CompareTo(lumB);
    });

    return _BuildRemappedResult(palette, indices);
  }

  private static (Color[] newPalette, byte[] remapTable) _SpatialLocalitySort(Color[] palette, byte[] pixels) {
    // Order palette entries by first-occurrence position in pixel stream
    var firstOccurrence = new int[palette.Length];
    Array.Fill(firstOccurrence, int.MaxValue);

    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] < palette.Length && firstOccurrence[pixels[i]] == int.MaxValue)
        firstOccurrence[pixels[i]] = i;

    var indices = Enumerable.Range(0, palette.Length).ToArray();
    Array.Sort(indices, (a, b) => firstOccurrence[a].CompareTo(firstOccurrence[b]));

    return _BuildRemappedResult(palette, indices);
  }

  private static (Color[] newPalette, byte[] remapTable) _LzwRunAwareSort(Color[] palette, byte[] pixels) {
    // Build adjacency weights: which palette indices appear next to each other most often
    var adjacency = new int[palette.Length, palette.Length];
    for (var i = 1; i < pixels.Length; ++i) {
      var a = pixels[i - 1];
      var b = pixels[i];
      if (a < palette.Length && b < palette.Length && a != b) {
        ++adjacency[a, b];
        ++adjacency[b, a];
      }
    }

    // Greedy nearest-neighbor traversal starting from most frequent color
    var freq = new int[palette.Length];
    foreach (var p in pixels)
      if (p < freq.Length)
        ++freq[p];

    var startIndex = 0;
    var maxFreq = 0;
    for (var i = 0; i < palette.Length; ++i)
      if (freq[i] > maxFreq) {
        maxFreq = freq[i];
        startIndex = i;
      }

    var visited = new bool[palette.Length];
    var order = new int[palette.Length];
    var current = startIndex;

    for (var step = 0; step < palette.Length; ++step) {
      order[step] = current;
      visited[current] = true;

      var bestNext = -1;
      var bestWeight = -1;
      for (var j = 0; j < palette.Length; ++j)
        if (!visited[j] && adjacency[current, j] > bestWeight) {
          bestWeight = adjacency[current, j];
          bestNext = j;
        }

      if (bestNext < 0)
        // Pick first unvisited
        for (var j = 0; j < palette.Length; ++j)
          if (!visited[j]) {
            bestNext = j;
            break;
          }

      current = bestNext >= 0 ? bestNext : 0;
    }

    return _BuildRemappedResult(palette, order);
  }

  private static (Color[] newPalette, byte[] remapTable) _HilbertCurveSort(Color[] palette) {
    var indices = Enumerable.Range(0, palette.Length).ToArray();
    Array.Sort(indices, (a, b) => {
      var hA = _RgbToHilbert(palette[a].R, palette[a].G, palette[a].B);
      var hB = _RgbToHilbert(palette[b].R, palette[b].G, palette[b].B);
      return hA.CompareTo(hB);
    });

    return _BuildRemappedResult(palette, indices);
  }

  private static (Color[] newPalette, byte[] remapTable) _CompressionOptimizedSort(Color[] palette, byte[] pixels) {
    // Try each heuristic ordering and pick the one that produces the smallest LZW output
    var candidates = new[] {
      PaletteReorderStrategy.FrequencySorted,
      PaletteReorderStrategy.LuminanceSorted,
      PaletteReorderStrategy.SpatialLocality,
      PaletteReorderStrategy.LzwRunAware,
      PaletteReorderStrategy.HilbertCurve
    };

    Color[]? bestPalette = null;
    byte[]? bestRemap = null;
    var bestSize = int.MaxValue;

    foreach (var strategy in candidates) {
      var (candidatePalette, candidateRemap) = Reorder(palette, pixels, strategy);
      var remappedPixels = ApplyRemap(pixels, candidateRemap);
      var compressed = LzwCompressor.Compress(remappedPixels, 8);

      if (compressed.Length >= bestSize)
        continue;

      bestSize = compressed.Length;
      bestPalette = candidatePalette;
      bestRemap = candidateRemap;
    }

    return bestPalette != null ? (bestPalette, bestRemap!) : (palette, _IdentityRemap(palette.Length));
  }

  private static (Color[] newPalette, byte[] remapTable) _BuildRemappedResult(Color[] palette, int[] newOrder) {
    var newPalette = new Color[palette.Length];
    var remapTable = new byte[palette.Length];

    for (var newIndex = 0; newIndex < newOrder.Length; ++newIndex) {
      var oldIndex = newOrder[newIndex];
      newPalette[newIndex] = palette[oldIndex];
      remapTable[oldIndex] = (byte)newIndex;
    }

    return (newPalette, remapTable);
  }

  /// <summary>Approximate 3D Hilbert curve distance for RGB values.</summary>
  private static long _RgbToHilbert(byte r, byte g, byte b) {
    // Simple 3D Hilbert approximation using bit-interleaving (Morton/Z-order as proxy)
    long result = 0;
    for (var i = 7; i >= 0; --i)
      result = (result << 3) | (((r >> i) & 1) << 2) | (((g >> i) & 1) << 1) | ((b >> i) & 1);

    return result;
  }
}
