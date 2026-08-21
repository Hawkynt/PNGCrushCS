using System;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Fills a transform block from the reconstructed samples above and to its left
/// (specification 8.5.1).
/// </summary>
/// <remarks>
/// Ten modes: a flat fill from the average, two straight copies, six directional filters and the
/// "true motion" mode that adds the vertical and horizontal gradients together. The directional ones
/// interpolate along the direction they name, which is why the diagonals read as many as twice their
/// own width of samples from the row above.
/// <para/>
/// Where those samples do not exist, the format invents them rather than changing mode. A block with
/// nothing above it predicts from a row of 127 and one with nothing to its left from a column of 129 —
/// values one either side of mid grey, chosen so that a block at the very top left corner comes out
/// flat under every mode rather than showing the seam between the two invented edges.
/// <para/>
/// The prediction is written straight into the frame rather than into a temporary. Several of the
/// modes are defined by copying from parts of the prediction already computed, and the edge samples
/// they need have been taken into local buffers before any of it is written, so there is nothing the
/// block can overwrite that it will later want to read.
/// </remarks>
internal static class Vp9IntraPrediction {

  /// <summary>
  /// The row above a block, indexed as the specification indexes it — from -1, the corner.
  /// </summary>
  /// <remarks>
  /// Three of the directional modes turn the corner between the row above and the column to the left,
  /// and read the sample diagonally above and left of the block to do it. Writing that as index -1 is
  /// the specification's notation; a wrapper that shifts by one keeps the arithmetic in the modes
  /// readable as the standard states it rather than as an implementation detail plus one.
  /// </remarks>
  private readonly ref struct EdgeRow {

    private readonly Span<int> _store;

    internal EdgeRow(Span<int> store) => this._store = store;

    internal int this[int index] {
      get => this._store[index + 1];
      set => this._store[index + 1] = value;
    }
  }

  /// <summary>
  /// Predicts one transform block.
  /// </summary>
  /// <param name="plane">The plane being reconstructed.</param>
  /// <param name="stride">Its row stride.</param>
  /// <param name="x">Column of the block's top left sample.</param>
  /// <param name="y">Row of the block's top left sample.</param>
  /// <param name="sizeLog2">Base two logarithm of the block's width.</param>
  /// <param name="mode">Which of the ten modes to use.</param>
  /// <param name="haveLeft">Whether there are reconstructed samples to the left.</param>
  /// <param name="haveAbove">Whether there are reconstructed samples above.</param>
  /// <param name="notOnRight">Whether the block has a neighbour to its right within the same block.</param>
  /// <param name="maxX">The last column of the plane that holds a coded sample.</param>
  /// <param name="maxY">The last row of the plane that holds a coded sample.</param>
  internal static void Predict(
    byte[] plane, int stride, int x, int y, int sizeLog2, int mode,
    bool haveLeft, bool haveAbove, bool notOnRight, int maxX, int maxY) {
    var size = 1 << sizeLog2;

    Span<int> aboveStore = stackalloc int[2 * 32 + 1];
    Span<int> left = stackalloc int[32];
    var above = new EdgeRow(aboveStore);

    _GatherAbove(plane, stride, x, y, size, sizeLog2, haveAbove, haveLeft, notOnRight, maxX, above);
    _GatherLeft(plane, stride, x, y, size, haveLeft, maxY, left);

    switch (mode) {
      case V_PRED: _PredictVertical(plane, stride, x, y, size, above); return;
      case H_PRED: _PredictHorizontal(plane, stride, x, y, size, left); return;
      case D207_PRED: _PredictDown207(plane, stride, x, y, size, left); return;
      case D45_PRED: _PredictDown45(plane, stride, x, y, size, above); return;
      case D63_PRED: _PredictDown63(plane, stride, x, y, size, above); return;
      case D117_PRED: _PredictDown117(plane, stride, x, y, size, above, left); return;
      case D135_PRED: _PredictDown135(plane, stride, x, y, size, above, left); return;
      case D153_PRED: _PredictDown153(plane, stride, x, y, size, above, left); return;
      case TM_PRED: _PredictTrueMotion(plane, stride, x, y, size, above, left); return;
      default: _PredictAverage(plane, stride, x, y, size, sizeLog2, haveAbove, haveLeft, above, left); return;
    }
  }

  // ============================================================================================
  // The edges
  // ============================================================================================

  private static void _GatherAbove(
    byte[] plane, int stride, int x, int y, int size, int sizeLog2,
    bool haveAbove, bool haveLeft, bool notOnRight, int maxX, EdgeRow above) {
    if (!haveAbove) {
      for (var i = -1; i < 2 * size; ++i)
        above[i] = 127;

      return;
    }

    var row = (y - 1) * stride;
    for (var i = 0; i < size; ++i)
      above[i] = plane[row + Math.Min(maxX, x + i)];

    // The second half of the row is only real for a 4x4 transform that has a neighbour to its right;
    // every larger transform, and every one on the right-hand edge of its block, extends the last
    // real sample rather than reading samples that have not been reconstructed.
    if (notOnRight && sizeLog2 == 2)
      for (var i = size; i < 2 * size; ++i)
        above[i] = plane[row + Math.Min(maxX, x + i)];
    else
      for (var i = size; i < 2 * size; ++i)
        above[i] = above[size - 1];

    above[-1] = haveLeft ? plane[row + Math.Min(maxX, x - 1)] : 129;
  }

  private static void _GatherLeft(
    byte[] plane, int stride, int x, int y, int size, bool haveLeft, int maxY, Span<int> left) {
    if (!haveLeft) {
      for (var i = 0; i < size; ++i)
        left[i] = 129;

      return;
    }

    for (var i = 0; i < size; ++i)
      left[i] = plane[Math.Min(maxY, y + i) * stride + x - 1];
  }

  // ============================================================================================
  // The modes
  // ============================================================================================

  private static int _Round2(int value, int bits) => (value + (1 << (bits - 1))) >> bits;

  private static void _Write(byte[] plane, int stride, int x, int y, int row, int column, int value)
    => plane[(y + row) * stride + x + column] = (byte)value;

  private static int _Read(byte[] plane, int stride, int x, int y, int row, int column)
    => plane[(y + row) * stride + x + column];

  private static void _PredictVertical(byte[] plane, int stride, int x, int y, int size, EdgeRow above) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, above[j]);
  }

  private static void _PredictHorizontal(byte[] plane, int stride, int x, int y, int size, ReadOnlySpan<int> left) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, left[i]);
  }

  private static void _PredictDown207(byte[] plane, int stride, int x, int y, int size, ReadOnlySpan<int> left) {
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, size - 1, j, left[size - 1]);

    for (var i = 0; i < size - 1; ++i)
      _Write(plane, stride, x, y, i, 0, _Round2(left[i] + left[i + 1], 1));

    for (var i = 0; i < size - 2; ++i)
      _Write(plane, stride, x, y, i, 1, _Round2(left[i] + 2 * left[i + 1] + left[i + 2], 2));

    _Write(plane, stride, x, y, size - 2, 1, _Round2(left[size - 2] + 3 * left[size - 1], 2));

    // Upwards through the rows, because each one is the row below it shifted two columns along.
    for (var i = size - 2; i >= 0; --i)
    for (var j = 2; j < size; ++j)
      _Write(plane, stride, x, y, i, j, _Read(plane, stride, x, y, i + 1, j - 2));
  }

  private static void _PredictDown45(byte[] plane, int stride, int x, int y, int size, EdgeRow above) {
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j,
        i + j + 2 < size * 2
          ? _Round2(above[i + j] + above[i + j + 1] * 2 + above[i + j + 2], 2)
          : above[2 * size - 1]);
  }

  private static void _PredictDown63(byte[] plane, int stride, int x, int y, int size, EdgeRow above) {
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
    byte[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
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
    byte[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
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
    byte[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
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
    byte[] plane, int stride, int x, int y, int size, EdgeRow above, ReadOnlySpan<int> left) {
    var corner = above[-1];
    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, Math.Clamp(above[j] + left[i] - corner, 0, 255));
  }

  private static void _PredictAverage(
    byte[] plane, int stride, int x, int y, int size, int sizeLog2,
    bool haveAbove, bool haveLeft, EdgeRow above, ReadOnlySpan<int> left) {
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
      average = 128;

    for (var i = 0; i < size; ++i)
    for (var j = 0; j < size; ++j)
      _Write(plane, stride, x, y, i, j, average);
  }
}
