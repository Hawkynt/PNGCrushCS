using System;

namespace FileFormat.Tiff;

/// <summary>Horizontal differencing predictor (TIFF predictor=2).</summary>
internal static class HorizontalDifferencing {

  /// <summary>Applies horizontal differencing in-place (encode direction).</summary>
  public static void Apply(byte[] data, int bytesPerRow, int rows, int samplesPerPixel) {
    for (var row = 0; row < rows; ++row) {
      var rowStart = row * bytesPerRow;
      for (var x = bytesPerRow - 1; x >= samplesPerPixel; --x)
        data[rowStart + x] = (byte)(data[rowStart + x] - data[rowStart + x - samplesPerPixel]);
    }
  }

  /// <summary>Reverses horizontal differencing in-place (decode direction).</summary>
  public static void Reverse(byte[] data, int bytesPerRow, int rows, int samplesPerPixel) {
    for (var row = 0; row < rows; ++row) {
      var rowStart = row * bytesPerRow;
      for (var x = samplesPerPixel; x < bytesPerRow; ++x)
        data[rowStart + x] = (byte)(data[rowStart + x] + data[rowStart + x - samplesPerPixel]);
    }
  }

  /// <summary>Applies horizontal differencing to a span (encode, single row).</summary>
  public static void Apply(Span<byte> row, int samplesPerPixel) {
    for (var x = row.Length - 1; x >= samplesPerPixel; --x)
      row[x] = (byte)(row[x] - row[x - samplesPerPixel]);
  }

  /// <summary>Reverses horizontal differencing on a span (decode, single row).</summary>
  public static void Reverse(Span<byte> row, int samplesPerPixel) {
    for (var x = samplesPerPixel; x < row.Length; ++x)
      row[x] = (byte)(row[x] + row[x - samplesPerPixel]);
  }
}
