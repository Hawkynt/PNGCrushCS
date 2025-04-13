using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PngOptimizer;
internal static class FilterTools {

  /// <summary>Applies the selected filters to the image data and returns the filtered data</summary>
  public static byte[][] ApplyFilters(byte[][] imageData, FilterType[] filters, int bytesPerPixel) {
    var filteredData = new byte[imageData.Length][];

    byte[]? token = null;
    try {
      Span<byte> filteredLine=default;
      for (var y = 0; y < imageData.Length; ++y) {
        var scanline = imageData[y];
        var previousScanline = y > 0 ? imageData[y - 1] : null;
        
        var filteredScanline = new byte[scanline.Length + 1];
        filteredScanline[0] = (byte)filters[y];

        if (token == null) {
          token = ArrayPool<byte>.Shared.Rent(scanline.Length);
          filteredLine = token.AsSpan(0, scanline.Length);
        }

        ApplyFilterTo(filters[y], scanline, previousScanline, bytesPerPixel,filteredLine);
        filteredLine.CopyTo(filteredScanline.AsSpan(1..));

        filteredData[y] = filteredScanline;
      }
    } finally {
      if(token!=null)
        ArrayPool<byte>.Shared.Return(token);
    }

    return filteredData;
  }

  public static void ApplyFilterTo(FilterType filterType, ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, int bytesPerPixel, Span<byte> target) {
    switch (filterType) {
      case FilterType.None:
        scanline.CopyTo(target);
        break;

      case FilterType.Sub:
        ApplySubFilter(scanline, target, bytesPerPixel);
        break;

      case FilterType.Up:
        ApplyUpFilter(scanline, previousScanline, target);
        break;

      case FilterType.Average:
        ApplyAverageFilter(scanline, previousScanline, target, bytesPerPixel);
        break;

      case FilterType.Paeth:
        ApplyPaethFilter(scanline, previousScanline, target, bytesPerPixel);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(filterType), filterType, null);
    }
  }

  /// <summary>Applies the specified filter to the scanline</summary>
  public static byte[] ApplyFilter(FilterType filterType, ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, int bytesPerPixel) {
    var result = new byte[scanline.Length];
    ApplyFilterTo(filterType,scanline,previousScanline,bytesPerPixel,result);
    return result;
  }

  /// <summary>Applies the Sub filter (type 1)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplySubFilter(ReadOnlySpan<byte> scanline, Span<byte> result, int bytesPerPixel) {
    scanline[..bytesPerPixel].CopyTo(result);
    for (var i = bytesPerPixel; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - scanline[i - bytesPerPixel]);
  }

  /// <summary>Applies the Up filter (type 2)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyUpFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result) {
    if (previousScanline.IsEmpty) {
      scanline.CopyTo(result);
      return;
    }

    for (var i = 0; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - previousScanline[i]);
  }

  /// <summary>Applies the Average filter (type 3)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyAverageFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result, int bytesPerPixel) {
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - (above >> 1)); // Divide by 2 with bit shift
    }

    for (var i = bytesPerPixel; i < scanline.Length; ++i) {
      var left = scanline[i - bytesPerPixel] & 0xFF;
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i] & 0xFF;
      var average = (left + above) >> 1; // Divide by 2 with bit shift
      result[i] = (byte)(scanline[i] - average);
    }
  }

  /// <summary>Applies the Paeth filter (type 4)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void ApplyPaethFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result, int bytesPerPixel) {
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - above);
    }

    // For remaining bytes, use the Paeth predictor
    for (var i = bytesPerPixel; i < scanline.Length; ++i) {
      var a = scanline[i - bytesPerPixel] & 0xFF; // Left
      var b = previousScanline.IsEmpty ? 0 : previousScanline[i] & 0xFF; // Above
      var c = previousScanline.IsEmpty ? 0 : previousScanline[i - bytesPerPixel] & 0xFF; // Upper left

      var p = a + b - c; // Initial estimate
      var pa = (p - a).Abs(); // Distance to a
      var pb = (p - b).Abs(); // Distance to b
      var pc = (p - c).Abs(); // Distance to c

      // Return nearest of a, b, c, breaking ties in order a, b, c
      var pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;

      result[i] = (byte)(scanline[i] - pr);
    }
  }

}
