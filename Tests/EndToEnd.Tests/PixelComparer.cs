using System;
using FileFormat.Core;

namespace EndToEnd.Tests;

/// <summary>Pixel-by-pixel comparison between two RawImages, both normalized to Bgra32.</summary>
internal static class PixelComparer {

  internal readonly record struct ComparisonResult(
    bool AreEqual,
    int MismatchCount,
    int TotalPixels,
    int MaxChannelDelta,
    (int X, int Y, byte[] Expected, byte[] Actual)? FirstMismatch
  );

  /// <summary>Compares two images pixel-by-pixel. Both are converted to Bgra32 for comparison.</summary>
  internal static ComparisonResult Compare(RawImage expected, RawImage actual, int tolerance = 0) {
    if (expected.Width != actual.Width || expected.Height != actual.Height)
      return new(false, expected.Width * expected.Height, expected.Width * expected.Height, 255,
        (0, 0, [], []));

    var expBgra = expected.ToBgra32();
    var actBgra = actual.ToBgra32();
    var w = expected.Width;
    var h = expected.Height;
    var mismatches = 0;
    var maxDelta = 0;
    (int X, int Y, byte[] Expected, byte[] Actual)? first = null;

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 4;
        var dB = Math.Abs(expBgra[i] - actBgra[i]);
        var dG = Math.Abs(expBgra[i + 1] - actBgra[i + 1]);
        var dR = Math.Abs(expBgra[i + 2] - actBgra[i + 2]);
        var dA = Math.Abs(expBgra[i + 3] - actBgra[i + 3]);
        var channelMax = Math.Max(Math.Max(dB, dG), Math.Max(dR, dA));
        maxDelta = Math.Max(maxDelta, channelMax);

        if (channelMax > tolerance) {
          mismatches++;
          first ??= (x, y, [expBgra[i], expBgra[i + 1], expBgra[i + 2], expBgra[i + 3]],
                           [actBgra[i], actBgra[i + 1], actBgra[i + 2], actBgra[i + 3]]);
        }
      }

    return new(mismatches == 0, mismatches, w * h, maxDelta, first);
  }

  /// <summary>Assert helper — throws NUnit assertion with detailed mismatch info.</summary>
  internal static void AssertEqual(RawImage expected, RawImage actual, int tolerance = 0, string? message = null) {
    var result = Compare(expected, actual, tolerance);
    if (result.AreEqual) return;

    var prefix = message != null ? $"{message}: " : "";
    var first = result.FirstMismatch!.Value;
    Assert.Fail(
      $"{prefix}{result.MismatchCount}/{result.TotalPixels} pixels differ (max delta={result.MaxChannelDelta}). " +
      $"First mismatch at ({first.X},{first.Y}): " +
      $"expected BGRA=[{first.Expected[0]},{first.Expected[1]},{first.Expected[2]},{first.Expected[3]}] " +
      $"actual BGRA=[{first.Actual[0]},{first.Actual[1]},{first.Actual[2]},{first.Actual[3]}]"
    );
  }
}
