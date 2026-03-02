using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PngOptimizer;

/// <summary>Palette reordering strategies for improved deflate compression of indexed PNG data</summary>
internal static class PngPaletteReorderer {
  /// <summary>Reorder palette using Hilbert curve sort (3D color-space locality)</summary>
  public static int[] HilbertSort(List<(byte R, byte G, byte B, byte A)> palette) {
    var indices = Enumerable.Range(0, palette.Count).ToArray();
    Array.Sort(indices, (a, b) => {
      var hA = _RgbToHilbert(palette[a].R, palette[a].G, palette[a].B);
      var hB = _RgbToHilbert(palette[b].R, palette[b].G, palette[b].B);
      return hA.CompareTo(hB);
    });
    return indices;
  }

  /// <summary>Reorder palette by first-occurrence position in pixel stream</summary>
  public static int[] SpatialLocalitySort(List<(byte R, byte G, byte B, byte A)> palette, byte[][] scanlines,
    int width, int bitDepth) {
    var firstOccurrence = new int[palette.Count];
    Array.Fill(firstOccurrence, int.MaxValue);

    var pixelsPerByte = 8 / bitDepth;
    var mask = (1 << bitDepth) - 1;
    var globalPixelIdx = 0;

    foreach (var scanline in scanlines)
      if (bitDepth == 8)
        foreach (var idx in scanline) {
          if (idx < palette.Count && firstOccurrence[idx] == int.MaxValue)
            firstOccurrence[idx] = globalPixelIdx;

          ++globalPixelIdx;
        }
      else
        foreach (var packed in scanline) {
          for (var bit = 0; bit < pixelsPerByte && globalPixelIdx < width * scanlines.Length; ++bit) {
            var shift = 8 - bitDepth * (bit + 1);
            var idx = (packed >> shift) & mask;
            if (idx < palette.Count && firstOccurrence[idx] == int.MaxValue)
              firstOccurrence[idx] = globalPixelIdx;

            ++globalPixelIdx;
          }
        }

    var indices = Enumerable.Range(0, palette.Count).ToArray();
    Array.Sort(indices, (a, b) => firstOccurrence[a].CompareTo(firstOccurrence[b]));
    return indices;
  }

  /// <summary>Try each ordering, filter+compress first 16 rows, pick the smallest</summary>
  public static int[] DeflateOptimizedSort(
    List<(byte R, byte G, byte B, byte A)> palette,
    byte[][] scanlines,
    int width,
    int bitDepth,
    int bytesPerPixel,
    int[] frequencySortOrder) {
    var sampleRows = Math.Min(16, scanlines.Length);

    var bestOrder = frequencySortOrder;
    var bestSize = _MeasureCompressedSize(scanlines, sampleRows, bytesPerPixel, frequencySortOrder, bitDepth,
      palette.Count);

    var hilbertOrder = HilbertSort(palette);
    var hilbertSize =
      _MeasureCompressedSize(scanlines, sampleRows, bytesPerPixel, hilbertOrder, bitDepth, palette.Count);
    if (hilbertSize < bestSize) {
      bestSize = hilbertSize;
      bestOrder = hilbertOrder;
    }

    var spatialOrder = SpatialLocalitySort(palette, scanlines, width, bitDepth);
    var spatialSize =
      _MeasureCompressedSize(scanlines, sampleRows, bytesPerPixel, spatialOrder, bitDepth, palette.Count);
    if (spatialSize < bestSize) bestOrder = spatialOrder;

    return bestOrder;
  }

  /// <summary>Apply palette reorder to scanline data</summary>
  public static void ApplyReorder(
    byte[][] scanlines,
    int[] newOrder,
    int paletteCount,
    int bitDepth,
    List<(byte R, byte G, byte B, byte A)> palette,
    byte[] paletteBytes,
    byte[]? tRNS,
    Dictionary<long, int> uniqueColors,
    bool includeAlpha) {
    // Build old-to-new remap
    var remap = new byte[paletteCount];
    var newPalette = new (byte R, byte G, byte B, byte A)[paletteCount];
    for (var newIdx = 0; newIdx < newOrder.Length && newIdx < paletteCount; ++newIdx) {
      var oldIdx = newOrder[newIdx];
      if (oldIdx >= paletteCount)
        continue;

      remap[oldIdx] = (byte)newIdx;
      newPalette[newIdx] = palette[oldIdx];
    }

    // Update palette list, palette bytes, and tRNS
    for (var i = 0; i < paletteCount; ++i) {
      palette[i] = newPalette[i];
      paletteBytes[i * 3] = newPalette[i].R;
      paletteBytes[i * 3 + 1] = newPalette[i].G;
      paletteBytes[i * 3 + 2] = newPalette[i].B;
    }

    if (tRNS != null)
      for (var i = 0; i < tRNS.Length && i < paletteCount; ++i)
        tRNS[i] = newPalette[i].A;

    // Update uniqueColors dictionary
    var keys = new List<long>(uniqueColors.Keys);
    foreach (var key in keys)
      if (uniqueColors[key] < paletteCount)
        uniqueColors[key] = remap[uniqueColors[key]];

    // Remap scanline data
    var pixelsPerByte = 8 / bitDepth;
    var mask = (1 << bitDepth) - 1;
    foreach (var scanline in scanlines)
      if (bitDepth == 8) {
        for (var x = 0; x < scanline.Length; ++x)
          if (scanline[x] < paletteCount)
            scanline[x] = remap[scanline[x]];
      } else {
        for (var byteIdx = 0; byteIdx < scanline.Length; ++byteIdx) {
          var packed = scanline[byteIdx];
          byte newPacked = 0;
          for (var bit = 0; bit < pixelsPerByte; ++bit) {
            var shift = 8 - bitDepth * (bit + 1);
            var idx = (packed >> shift) & mask;
            var newIdx = idx < paletteCount ? remap[idx] : idx;
            newPacked |= (byte)((newIdx & mask) << shift);
          }

          scanline[byteIdx] = newPacked;
        }
      }
  }

  private static long _MeasureCompressedSize(byte[][] scanlines, int rowCount, int bytesPerPixel, int[] order,
    int bitDepth, int paletteCount) {
    // Build remap
    var remap = new byte[Math.Max(256, paletteCount)];
    for (var i = 0; i < order.Length && i < paletteCount; ++i)
      if (order[i] < paletteCount)
        remap[order[i]] = (byte)i;

    var pixelsPerByte = 8 / bitDepth;
    var mask = (1 << bitDepth) - 1;

    using var ms = new MemoryStream();
    using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest, true)) {
      for (var y = 0; y < rowCount; ++y) {
        var scanline = scanlines[y];
        // Write filter byte (None)
        zlib.WriteByte(0);
        if (bitDepth == 8)
          foreach (var @byte in scanline)
            zlib.WriteByte(@byte < paletteCount ? remap[@byte] : @byte);
        else
          foreach (var packed in scanline) {
            byte newPacked = 0;
            for (var bit = 0; bit < pixelsPerByte; ++bit) {
              var shift = 8 - bitDepth * (bit + 1);
              var idx = (packed >> shift) & mask;
              var newIdx = idx < paletteCount ? remap[idx] : idx;
              newPacked |= (byte)((newIdx & mask) << shift);
            }

            zlib.WriteByte(newPacked);
          }
      }
    }

    return ms.Length;
  }

  /// <summary>Build identity order (used as baseline for frequency sort)</summary>
  public static int[] IdentityOrder(int count) {
    var order = new int[count];
    for (var i = 0; i < count; ++i)
      order[i] = i;
    return order;
  }

  /// <summary>Approximate 3D Hilbert curve distance for RGB values (Morton/Z-order proxy)</summary>
  private static long _RgbToHilbert(byte r, byte g, byte b) {
    long result = 0;
    for (var i = 7; i >= 0; --i)
      result = (result << 3) | (((r >> i) & 1) << 2) | (((g >> i) & 1) << 1) | ((b >> i) & 1);

    return result;
  }
}
