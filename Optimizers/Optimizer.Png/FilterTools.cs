using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using FileFormat.Png;

namespace Optimizer.Png;

internal static class FilterTools {
  internal static readonly PngFilterType[] AllPngFilterTypes = Enum.GetValues<PngFilterType>();

  /// <summary>Applies the selected filters to the image data and returns the filtered data</summary>
  public static byte[][] ApplyFilters(byte[][] imageData, PngFilterType[] filters, int bytesPerPixel) {
    var filteredData = new byte[imageData.Length][];

    byte[]? token = null;
    try {
      Span<byte> filteredLine = default;
      for (var y = 0; y < imageData.Length; ++y) {
        var scanline = imageData[y];
        var previousScanline = y > 0 ? imageData[y - 1] : null;

        var filteredScanline = new byte[scanline.Length + 1];
        filteredScanline[0] = (byte)filters[y];

        if (token == null) {
          token = ArrayPool<byte>.Shared.Rent(scanline.Length);
          filteredLine = token.AsSpan(0, scanline.Length);
        }

        ApplyFilterTo(filters[y], scanline, previousScanline, bytesPerPixel, filteredLine);
        filteredLine.CopyTo(filteredScanline.AsSpan(1..));

        filteredData[y] = filteredScanline;
      }
    } finally {
      if (token != null)
        ArrayPool<byte>.Shared.Return(token);
    }

    return filteredData;
  }

  public static void ApplyFilterTo(PngFilterType filterType, ReadOnlySpan<byte> scanline,
    ReadOnlySpan<byte> previousScanline, int bytesPerPixel, Span<byte> target) {
    switch (filterType) {
      case PngFilterType.None:
        scanline.CopyTo(target);
        break;

      case PngFilterType.Sub:
        ApplySubFilter(scanline, target, bytesPerPixel);
        break;

      case PngFilterType.Up:
        ApplyUpFilter(scanline, previousScanline, target);
        break;

      case PngFilterType.Average:
        ApplyAverageFilter(scanline, previousScanline, target, bytesPerPixel);
        break;

      case PngFilterType.Paeth:
        ApplyPaethFilter(scanline, previousScanline, target, bytesPerPixel);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(filterType), filterType, null);
    }
  }

  /// <summary>Applies the specified filter to the scanline</summary>
  public static byte[] ApplyFilter(PngFilterType filterType, ReadOnlySpan<byte> scanline,
    ReadOnlySpan<byte> previousScanline, int bytesPerPixel) {
    var result = new byte[scanline.Length];
    ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, result);
    return result;
  }

  /// <summary>Applies the Sub filter (type 1) with SIMD acceleration</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplySubFilter(ReadOnlySpan<byte> scanline, Span<byte> result, int bytesPerPixel) {
    scanline[..bytesPerPixel].CopyTo(result);

    var i = bytesPerPixel;
    if (Vector.IsHardwareAccelerated && scanline.Length >= Vector<byte>.Count + bytesPerPixel) {
      var vectorSize = Vector<byte>.Count;
      for (; i + vectorSize <= scanline.Length; i += vectorSize) {
        var current = new Vector<byte>(scanline[i..]);
        var left = new Vector<byte>(scanline[(i - bytesPerPixel)..]);
        (current - left).CopyTo(result[i..]);
      }
    }

    for (; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - scanline[i - bytesPerPixel]);
  }

  /// <summary>Applies the Up filter (type 2) with SIMD acceleration</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyUpFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline,
    Span<byte> result) {
    if (previousScanline.IsEmpty) {
      scanline.CopyTo(result);
      return;
    }

    var i = 0;
    if (Vector.IsHardwareAccelerated && scanline.Length >= Vector<byte>.Count) {
      var vectorSize = Vector<byte>.Count;
      for (; i + vectorSize <= scanline.Length; i += vectorSize) {
        var current = new Vector<byte>(scanline[i..]);
        var above = new Vector<byte>(previousScanline[i..]);
        (current - above).CopyTo(result[i..]);
      }
    }

    for (; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - previousScanline[i]);
  }

  /// <summary>Applies the Average filter (type 3) with SIMD acceleration</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyAverageFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline,
    Span<byte> result, int bytesPerPixel) {
    // First bytesPerPixel bytes: left=0, so average = above/2
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - (above >> 1));
    }

    var idx = bytesPerPixel;

    if (Vector.IsHardwareAccelerated && !previousScanline.IsEmpty &&
        scanline.Length >= Vector<byte>.Count + bytesPerPixel) {
      var vectorSize = Vector<byte>.Count;
      for (; idx + vectorSize <= scanline.Length; idx += vectorSize) {
        var current = new Vector<byte>(scanline[idx..]);
        var left = new Vector<byte>(scanline[(idx - bytesPerPixel)..]);
        var above = new Vector<byte>(previousScanline[idx..]);
        // Overflow-safe floor average: avg(a,b) = (a & b) + ((a ^ b) >>> 1)
        var avg = (left & above) + Vector.ShiftRightLogical(left ^ above, 1);
        (current - avg).CopyTo(result[idx..]);
      }
    }

    for (; idx < scanline.Length; ++idx) {
      var left = scanline[idx - bytesPerPixel] & 0xFF;
      var above = previousScanline.IsEmpty ? 0 : previousScanline[idx] & 0xFF;
      var average = (left + above) >> 1;
      result[idx] = (byte)(scanline[idx] - average);
    }
  }

  /// <summary>Applies the Paeth filter (type 4) with SIMD acceleration</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyPaethFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline,
    Span<byte> result, int bytesPerPixel) {
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - above);
    }

    var idx = bytesPerPixel;

    if (Vector.IsHardwareAccelerated && !previousScanline.IsEmpty &&
        scanline.Length >= Vector<byte>.Count + bytesPerPixel) {
      var vectorSize = Vector<byte>.Count;
      for (; idx + vectorSize <= scanline.Length; idx += vectorSize) {
        var current = new Vector<byte>(scanline[idx..]);
        var left = new Vector<byte>(scanline[(idx - bytesPerPixel)..]);
        var above = new Vector<byte>(previousScanline[idx..]);
        var aboveLeft = new Vector<byte>(previousScanline[(idx - bytesPerPixel)..]);

        // Widen to ushort for signed arithmetic
        Vector.Widen(current, out var curLo, out var curHi);
        Vector.Widen(left, out var aLo, out var aHi);
        Vector.Widen(above, out var bLo, out var bHi);
        Vector.Widen(aboveLeft, out var cLo, out var cHi);

        // Compute Paeth predictor in ushort domain
        var predLo = _PaethPredictor(aLo, bLo, cLo);
        var predHi = _PaethPredictor(aHi, bHi, cHi);

        // Narrow back and subtract
        var predictor = Vector.Narrow(predLo, predHi);
        (current - predictor).CopyTo(result[idx..]);
      }
    }

    // Scalar tail
    for (; idx < scanline.Length; ++idx) {
      var a = scanline[idx - bytesPerPixel] & 0xFF;
      var b = previousScanline.IsEmpty ? 0 : previousScanline[idx] & 0xFF;
      var c = previousScanline.IsEmpty ? 0 : previousScanline[idx - bytesPerPixel] & 0xFF;

      var p = a + b - c;
      var pa = (p - a).Abs();
      var pb = (p - b).Abs();
      var pc = (p - c).Abs();

      var pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      result[idx] = (byte)(scanline[idx] - pr);
    }
  }

  /// <summary>Compute Paeth predictor for a vector lane: nearest of a, b, c to p = a + b - c</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Vector<ushort> _PaethPredictor(Vector<ushort> a, Vector<ushort> b, Vector<ushort> c) {
    // Compute absolute differences without underflow by avoiding p = a + b - c directly.
    // |p - a| = |b - c|, |p - b| = |a - c|, |p - c| = |a + b - 2c|
    // All a, b, c are in [0, 255] so Max-Min is safe in ushort.
    var pa = Vector.Max(b, c) - Vector.Min(b, c);
    var pb = Vector.Max(a, c) - Vector.Min(a, c);
    var sum = a + b;
    var dbl = c + c;
    var pc = Vector.Max(sum, dbl) - Vector.Min(sum, dbl);

    // pa <= pb && pa <= pc → a; pb <= pc → b; else c
    var useA = Vector.LessThanOrEqual(pa, pb) & Vector.LessThanOrEqual(pa, pc);
    var useB = Vector.LessThanOrEqual(pb, pc);
    var bc = Vector.ConditionalSelect(useB, b, c);
    return Vector.ConditionalSelect(useA, a, bc);
  }
}
