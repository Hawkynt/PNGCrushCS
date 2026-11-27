using System;

namespace FileFormat.LuraWave;

/// <summary>The inverse of LuraWave's reversible 5/3 lifting wavelet, working on the in-place coefficient layout.</summary>
/// <remarks>
/// Each level is undone column-wise first, then row-wise. Within a line the low-pass samples sit at even and the
/// high-pass samples at odd positions; the edges mirror symmetrically. The coded grid is always a multiple of
/// <c>2^levels</c>, so every line has an even length.
/// </remarks>
internal static class LuraWaveWavelet {

  /// <summary>Undoes <paramref name="levels"/> levels in place for one channel of an interleaved plane.</summary>
  public static void Inverse(int[] plane, int width, int height, int channels, int channel, int levels) {
    var line = new int[Math.Max(width, height)];
    for (var level = levels; level >= 1; --level) {
      var step = 1 << (level - 1);
      var columns = width >> (level - 1);
      var rows = height >> (level - 1);
      var rowStride = width * channels;

      for (var column = 0; column < columns; ++column) {
        var first = column * step * channels + channel;
        Synthesize(plane, first, step * rowStride, rows, line);
      }

      for (var row = 0; row < rows; ++row) {
        var first = row * step * rowStride + channel;
        Synthesize(plane, first, step * channels, columns, line);
      }
    }
  }

  /// <summary>Applies <paramref name="levels"/> levels in place — rows first, then columns, so <see cref="Inverse"/> undoes it exactly.</summary>
  public static void Forward(int[] plane, int width, int height, int channels, int channel, int levels) {
    var line = new int[Math.Max(width, height)];
    for (var level = 1; level <= levels; ++level) {
      var step = 1 << (level - 1);
      var columns = width >> (level - 1);
      var rows = height >> (level - 1);
      var rowStride = width * channels;

      for (var row = 0; row < rows; ++row)
        Analyze(plane, row * step * rowStride + channel, step * channels, columns, line);

      for (var column = 0; column < columns; ++column)
        Analyze(plane, column * step * channels + channel, step * rowStride, rows, line);
    }
  }

  private static void Analyze(int[] data, int first, int stride, int count, int[] line) {
    var half = count >> 1;
    for (var i = 0; i < count; ++i)
      line[i] = data[first + i * stride];

    for (var k = 0; k < half; ++k) {
      var right = k + 1 < half ? line[2 * k + 2] : line[2 * k];
      line[2 * k + 1] -= (line[2 * k] + right + 1) >> 1;
    }

    for (var k = 0; k < half; ++k) {
      var left = line[k == 0 ? 1 : 2 * k - 1];
      line[2 * k] += (left + line[2 * k + 1] + 2) >> 2;
    }

    for (var i = 0; i < count; ++i)
      data[first + i * stride] = line[i];
  }

  /// <summary>One-dimensional 5/3 synthesis of <paramref name="count"/> samples spaced <paramref name="stride"/> apart.</summary>
  private static void Synthesize(int[] data, int first, int stride, int count, int[] line) {
    var half = count >> 1;
    for (var i = 0; i < count; ++i)
      line[i] = data[first + i * stride];

    // Undo the update step: even = low - (left detail + right detail + 2) / 4, the left edge mirroring.
    for (var k = 0; k < half; ++k) {
      var left = line[k == 0 ? 1 : 2 * k - 1];
      line[2 * k] -= (left + line[2 * k + 1] + 2) >> 2;
    }

    // Undo the predict step: odd = high + (left even + right even + 1) / 2, the right edge mirroring.
    for (var k = 0; k < half; ++k) {
      var right = k + 1 < half ? line[2 * k + 2] : line[2 * k];
      line[2 * k + 1] += (line[2 * k] + right + 1) >> 1;
    }

    for (var i = 0; i < count; ++i)
      data[first + i * stride] = line[i];
  }
}
