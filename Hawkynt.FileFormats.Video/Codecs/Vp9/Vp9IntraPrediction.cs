using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>Fills a transform block from reconstructed neighbours (VP9 specification 8.5.1).</summary>
internal static class Vp9IntraPrediction {

  private readonly ref struct EdgeRow {
    private readonly Span<int> _store;
    internal EdgeRow(Span<int> store) => this._store = store;
    internal int this[int index] {
      get => this._store[index + 1];
      set => this._store[index + 1] = value;
    }
  }

  internal static void Predict(
    ushort[] plane, int stride, int x, int y, int sizeLog2, int mode,
    bool haveLeft, bool haveAbove, bool notOnRight, int maxX, int maxY, int bitDepth) {
    var size = 1 << sizeLog2;
    var baseValue = 128 << (bitDepth - 8);
    var maxSample = (1 << bitDepth) - 1;

    Span<int> aboveStore = stackalloc int[2 * 32 + 1];
    Span<int> left = stackalloc int[32];
    var above = new EdgeRow(aboveStore);

    _GatherAbove(
      plane, stride, x, y, size, sizeLog2, haveAbove, haveLeft, notOnRight, maxX,
      baseValue, above);
    _GatherLeft(plane, stride, x, y, size, haveLeft, maxY, baseValue, left);

    switch (mode) {
      case V_PRED: _PredictVertical(plane, stride, x, y, size, above); return;
      case H_PRED: _PredictHorizontal(plane, stride, x, y, size, left); return;
      case D207_PRED: _PredictDown207(plane, stride, x, y, size, left); return;
      case D45_PRED: _PredictDown45(plane, stride, x, y, size, above); return;
      case D63_PRED: _PredictDown63(plane, stride, x, y, size, above); return;
      case D117_PRED: _PredictDown117(plane, stride, x, y, size, above, left); return;
      case D135_PRED: _PredictDown135(plane, stride, x, y, size, above, left); return;
      case D153_PRED: _PredictDown153(plane, stride, x, y, size, above, left); return;
      case TM_PRED: _PredictTrueMotion(plane, stride, x, y, size, above, left, maxSample); return;
      default: _PredictAverage(
        plane, stride, x, y, size, sizeLog2, haveAbove, haveLeft, above, left, baseValue); return;
    }
  }

  private static void _GatherAbove(
    ushort[] plane, int stride, int x, int y, int size, int sizeLog2,
    bool haveAbove, bool haveLeft, bool notOnRight, int maxX, int baseValue, EdgeRow above) {
    if (!haveAbove) {
      // libvpx build_intra_predictors_high(): 127 becomes base-1 at high bit depth.
      for (var i = -1; i < 2 * size; ++i)
        above[i] = baseValue - 1;
      return;
    }

    var row = (y - 1) * stride;
    for (var i = 0; i < size; ++i)
      above[i] = plane[row + Math.Min(maxX, x + i)];

    if (notOnRight && sizeLog2 == 2)
      for (var i = size; i < 2 * size; ++i)
        above[i] = plane[row + Math.Min(maxX, x + i)];
    else
      for (var i = size; i < 2 * size; ++i)
        above[i] = above[size - 1];

    above[-1] = haveLeft ? plane[row + Math.Min(maxX, x - 1)] : baseValue + 1;
  }

  private static void _GatherLeft(
    ushort[] plane, int stride, int x, int y, int size, bool haveLeft, int maxY,
    int baseValue, Span<int> left) {
    if (!haveLeft) {
      // libvpx build_intra_predictors_high(): 129 becomes base+1 at high bit depth.
      for (var i = 0; i < size; ++i)
        left[i] = baseValue + 1;
      return;
    }

    for (var i = 0; i < size; ++i)
      left[i] = plane[Math.Min(maxY, y + i) * stride + x - 1];
  }

  private static int _Round2(int value, int bits) => (value + (1 << (bits - 1))) >> bits;

  private static void _Write(ushort[] plane, int stride, int x, int y, int row, int column, int value)
    => plane[(y + row) * stride + x + column] = (ushort)value;

  private static int _Read(ushort[] plane, int stride, int x, int y, int row, int column)
    => plane[(y + row) * stride + x + column];

  private static void _PredictVertical(ushort[] plane, int stride, int x, int y, int size, EdgeRow above) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, above[j]);
  }

  private static void _PredictHorizontal(ushort[] plane, int stride, int x, int y, int size, ReadOnlySpan<int> left) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, left[i]);
  }

  private static void _PredictDown207(ushort[] plane, int stride, int x, int y, int size, ReadOnlySpan<int> left) {
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, size - 1, j, left[size - 1]);

    for (var i = 0; i < size - 1; ++i)
      _Write(plane, stride, x, y, i, 0, _Round2(left[i] + left[i + 1], 1));

    for (var i = 0; i < size - 2; ++i)
      _Write(plane, stride, x, y, i, 1, _Round2(left[i] + 2 * left[i + 1] + left[i + 2], 2));

    _Write(plane, stride, x, y, size - 2, 1, _Round2(left[size - 2] + 3 * left[size - 1], 2));

    for (var i = size - 2; i >= 0; --i)
    for (var j = 2; j < size; ++j)
      _Write(plane, stride, x, y, i, j, _Read(plane, stride, x, y, i + 1, j - 2));
  }

  private static void _PredictDown45(ushort[] plane, int stride, int x, int y, int size, EdgeRow above) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j,
        i + j + 2 < size * 2
          ? _Round2(above[i + j] + above[i + j + 1] * 2 + above[i + j + 2], 2)
          : above[2 * size - 1]);
  }

  private static void _PredictDown63(ushort[] plane, int stride, int x, int y, int size, EdgeRow above) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j) {
      var at = i / 2 + j;
      _Write(plane, stride, x, y, i, j,
        (i & 1) != 0
          ? _Round2(above[at] + above[at + 1] * 2 + above[at + 2], 2)
          : _Round2(above[at] + above[at + 1], 1));
    }
  }

  private static void _PredictDown117(
    ushort[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, 0, j, _Round2(above[j - 1] + above[j], 1));

    _Write(plane, stride, x, y, 1, 0, _Round2(left[0] + 2 * above[-1] + above[0], 2));
    for (var j = 1; j < size; ++j)
      _Write(plane, stride, x, y, 1, j, _Round2(above[j - 2] + 2 * above[j - 1] + above[j], 2));

    _Write(plane, stride, x, y, 2, 0, _Round2(above[-1] + 2 * left[0] + left[1], 2));

    for (var i = 3; i < size; ++i)
      _Write(plane, stride, x, y, i, 0, _Round2(left[i - 3] + 2 * left[i - 2] + left[i - 1], 2));

    for (var i = 2; i < size; ++i)
    for (var j = 1; j < size; ++j)
      _Write(plane, stride, x, y, i, j, _Read(plane, stride, x, y, i - 2, j - 1));
  }

  private static void _PredictDown135(
    ushort[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
    _Write(plane, stride, x, y, 0, 0, _Round2(left[0] + 2 * above[-1] + above[0], 2));
    for (var j = 1; j < size; ++j)
      _Write(plane, stride, x, y, 0, j, _Round2(above[j - 2] + 2 * above[j - 1] + above[j], 2));

    _Write(plane, stride, x, y, 1, 0, _Round2(above[-1] + 2 * left[0] + left[1], 2));
    for (var i = 2; i < size; ++i)
      _Write(plane, stride, x, y, i, 0, _Round2(left[i - 2] + 2 * left[i - 1] + left[i], 2));

    for (var i = 1; i < size; ++i)
    for (var j = 1; j < size; ++j)
      _Write(plane, stride, x, y, i, j, _Read(plane, stride, x, y, i - 1, j - 1));
  }

  private static void _PredictDown153(
    ushort[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
    _Write(plane, stride, x, y, 0, 0, _Round2(left[0] + above[-1], 1));
    for (var i = 1; i < size; ++i)
      _Write(plane, stride, x, y, i, 0, _Round2(left[i - 1] + left[i], 1));

    _Write(plane, stride, x, y, 0, 1, _Round2(left[0] + 2 * above[-1] + above[0], 2));
    _Write(plane, stride, x, y, 1, 1, _Round2(above[-1] + 2 * left[0] + left[1], 2));
    for (var i = 2; i < size; ++i)
      _Write(plane, stride, x, y, i, 1, _Round2(left[i - 2] + 2 * left[i - 1] + left[i], 2));

    for (var j = 2; j < size; ++j)
      _Write(plane, stride, x, y, 0, j, _Round2(above[j - 3] + 2 * above[j - 2] + above[j - 1], 2));

    for (var i = 1; i < size; ++i)
    for (var j = 2; j < size; ++j)
      _Write(plane, stride, x, y, i, j, _Read(plane, stride, x, y, i - 1, j - 2));
  }

  private static void _PredictTrueMotion(
    ushort[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left, int maxSample) {
    var corner = above[-1];
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, Math.Clamp(above[j] + left[i] - corner, 0, maxSample));
  }

  private static void _PredictAverage(
    ushort[] plane, int stride, int x, int y, int size, int sizeLog2,
    bool haveAbove, bool haveLeft, EdgeRow above, ReadOnlySpan<int> left, int baseValue) {
    int average;

    if (haveLeft && haveAbove) {
      var sum = 0;
      for (var k = 0; k < size; ++k)
        sum += left[k] + above[k];
      average = (sum + size) >> (sizeLog2 + 1);
    } else if (haveLeft) {
      var sum = 0;
      for (var k = 0; k < size; ++k)
        sum += left[k];
      average = (sum + (1 << (sizeLog2 - 1))) >> sizeLog2;
    } else if (haveAbove) {
      var sum = 0;
      for (var k = 0; k < size; ++k)
        sum += above[k];
      average = (sum + (1 << (sizeLog2 - 1))) >> sizeLog2;
    } else
      average = baseValue;

    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, average);
  }
}
