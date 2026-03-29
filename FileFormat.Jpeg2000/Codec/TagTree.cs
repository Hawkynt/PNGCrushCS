using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Tag tree for hierarchical coding of non-negative integer arrays in JPEG 2000 packet headers (ITU-T T.800 Section B.10.2).</summary>
internal sealed class TagTree {

  private readonly int[][] _values;
  private readonly int[][] _states;
  private readonly int[] _levelWidths;
  private readonly int[] _levelHeights;
  private readonly int _levels;

  public TagTree(int width, int height) {
    // Build multi-level tree from leaf dimensions upward until 1x1
    var widths = new System.Collections.Generic.List<int>();
    var heights = new System.Collections.Generic.List<int>();
    var w = width;
    var h = height;
    while (true) {
      widths.Add(w);
      heights.Add(h);
      if (w <= 1 && h <= 1)
        break;
      w = (w + 1) >> 1;
      h = (h + 1) >> 1;
    }

    _levels = widths.Count;
    _levelWidths = widths.ToArray();
    _levelHeights = heights.ToArray();
    _values = new int[_levels][];
    _states = new int[_levels][];
    for (var i = 0; i < _levels; ++i) {
      var count = _levelWidths[i] * _levelHeights[i];
      _values[i] = new int[count];
      _states[i] = new int[count];
    }
  }

  /// <summary>Decode the value at leaf position (x,y) up to the given threshold using raw bits from the reader.</summary>
  /// <param name="x">Leaf x coordinate.</param>
  /// <param name="y">Leaf y coordinate.</param>
  /// <param name="threshold">Threshold value to decode up to.</param>
  /// <param name="reader">Bit reader for raw packet header bits.</param>
  /// <returns><c>true</c> if the decoded value is at most the threshold.</returns>
  public bool Decode(int x, int y, int threshold, BitReader reader) {
    // Walk from root to leaf, updating states
    Span<int> indices = stackalloc int[_levels];
    var cx = x;
    var cy = y;
    for (var level = 0; level < _levels; ++level) {
      indices[level] = cy * _levelWidths[level] + cx;
      cx >>= 1;
      cy >>= 1;
    }

    // Start at the root (last level) and propagate downward
    var minValue = 0;
    for (var level = _levels - 1; level >= 0; --level) {
      var idx = indices[level];
      if (_states[level][idx] < minValue)
        _states[level][idx] = minValue;

      while (_states[level][idx] < threshold) {
        if (reader.ReadBit() != 0) {
          _values[level][idx] = _states[level][idx];
          break;
        }
        ++_states[level][idx];
      }

      if (_states[level][idx] >= threshold)
        return false;

      minValue = _values[level][idx];
    }

    return true;
  }

  /// <summary>Get the decoded value at leaf position (x,y).</summary>
  public int GetValue(int x, int y) => _values[0][y * _levelWidths[0] + x];

  /// <summary>Encode the value at leaf position (x,y) up to the given threshold using raw bits to the writer.</summary>
  public void Encode(int x, int y, int value, int threshold, BitWriter writer) {
    Span<int> indices = stackalloc int[_levels];
    var cx = x;
    var cy = y;
    for (var level = 0; level < _levels; ++level) {
      indices[level] = cy * _levelWidths[level] + cx;
      cx >>= 1;
      cy >>= 1;
    }

    // Set the leaf value
    _values[0][indices[0]] = value;

    // Propagate minimum to parent nodes
    for (var level = 1; level < _levels; ++level) {
      var idx = indices[level];
      if (_values[level][idx] == 0 || value < _values[level][idx])
        _values[level][idx] = value;
    }

    var minValue = 0;
    for (var level = _levels - 1; level >= 0; --level) {
      var idx = indices[level];
      if (_states[level][idx] < minValue)
        _states[level][idx] = minValue;

      while (_states[level][idx] < threshold && _states[level][idx] < value) {
        writer.WriteBit(0);
        ++_states[level][idx];
      }

      if (_states[level][idx] < threshold) {
        writer.WriteBit(1);
        break;
      }

      minValue = _states[level][idx];
    }
  }
}
