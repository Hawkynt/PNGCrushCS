using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>
/// Stateful JPEG 2000 tag tree used by packet-header inclusion and zero-bit-plane coding
/// (ITU-T T.800 B.10.2).
/// </summary>
/// <remarks>
/// A tag tree is not a unary integer written independently for each code-block. Parent nodes carry
/// the minimum of their children and, once a node's value has been signalled, later threshold probes
/// emit no more bits for that node. Both properties matter as soon as a subband contains more than
/// one code-block. The old implementation populated parents lazily while it was already emitting
/// them, so the first child could permanently publish a value that a later sibling should have made
/// smaller; it also stopped at the first signalled parent rather than descending to the leaf.
/// </remarks>
internal sealed class TagTree {

  private const int _UNKNOWN = int.MaxValue;

  private readonly int[][] _values;
  private readonly int[][] _states;
  private readonly bool[][] _known;
  private readonly int[] _levelWidths;
  private readonly int[] _levelHeights;
  private readonly int _levels;
  private bool _built;

  public TagTree(int width, int height) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height));

    var widths = new List<int>();
    var heights = new List<int>();
    for (var w = width, h = height;; w = (w + 1) >> 1, h = (h + 1) >> 1) {
      widths.Add(w);
      heights.Add(h);
      if (w == 1 && h == 1)
        break;
    }

    _levels = widths.Count;
    _levelWidths = widths.ToArray();
    _levelHeights = heights.ToArray();
    _values = new int[_levels][];
    _states = new int[_levels][];
    _known = new bool[_levels][];

    for (var level = 0; level < _levels; ++level) {
      var count = _levelWidths[level] * _levelHeights[level];
      _values[level] = new int[count];
      Array.Fill(_values[level], _UNKNOWN);
      _states[level] = new int[count];
      _known[level] = new bool[count];
    }
  }

  /// <summary>Assigns one encoder leaf value. All values must be assigned before the first encode.</summary>
  public void SetValue(int x, int y, int value) {
    if ((uint)x >= (uint)_levelWidths[0])
      throw new ArgumentOutOfRangeException(nameof(x));
    if ((uint)y >= (uint)_levelHeights[0])
      throw new ArgumentOutOfRangeException(nameof(y));
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    _values[0][y * _levelWidths[0] + x] = value;
    _built = false;
  }

  /// <summary>Returns the decoded or assigned leaf value, or <see cref="int.MaxValue"/> if unknown.</summary>
  public int GetValue(int x, int y) => _values[0][y * _levelWidths[0] + x];

  /// <summary>
  /// Decodes whether the leaf value is below <paramref name="thresholdExclusive"/>.
  /// Repeated calls with increasing thresholds continue from the previous tag-tree state.
  /// </summary>
  public bool Decode(int x, int y, int thresholdExclusive, BitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);
    if (thresholdExclusive <= 0)
      return false;

    Span<int> indices = stackalloc int[_levels];
    _BuildPath(x, y, indices);

    var lowerBound = 0;
    for (var level = _levels - 1; level >= 0; --level) {
      var index = indices[level];
      var state = Math.Max(_states[level][index], lowerBound);
      _states[level][index] = state;

      if (_known[level][index]) {
        var value = _values[level][index];
        if (value >= thresholdExclusive)
          return false;
        lowerBound = value;
        continue;
      }

      while (state < thresholdExclusive) {
        if (reader.ReadBit() != 0) {
          _values[level][index] = state;
          _known[level][index] = true;
          break;
        }

        ++state;
        _states[level][index] = state;
      }

      if (!_known[level][index])
        return false;

      lowerBound = _values[level][index];
    }

    return true;
  }

  /// <summary>
  /// Encodes whether the assigned leaf value is below <paramref name="thresholdExclusive"/>.
  /// </summary>
  /// <returns><c>true</c> if the leaf is below the threshold, otherwise <c>false</c>.</returns>
  public bool Encode(int x, int y, int thresholdExclusive, BitWriter writer) {
    ArgumentNullException.ThrowIfNull(writer);
    if (thresholdExclusive <= 0)
      return false;

    _BuildValues();

    Span<int> indices = stackalloc int[_levels];
    _BuildPath(x, y, indices);

    var lowerBound = 0;
    for (var level = _levels - 1; level >= 0; --level) {
      var index = indices[level];
      var value = _values[level][index];
      var state = Math.Max(_states[level][index], lowerBound);
      _states[level][index] = state;

      if (_known[level][index]) {
        if (value >= thresholdExclusive)
          return false;
        lowerBound = value;
        continue;
      }

      while (state < thresholdExclusive && state < value) {
        writer.WriteBit(0);
        ++state;
        _states[level][index] = state;
      }

      if (state >= thresholdExclusive)
        return false;

      // state == value < threshold: publish this node's exact value and descend. A previously
      // published node never emits the one again; _known carries that fact across later layers.
      writer.WriteBit(1);
      _known[level][index] = true;
      lowerBound = value;
    }

    return true;
  }

  private void _BuildPath(int x, int y, Span<int> indices) {
    if ((uint)x >= (uint)_levelWidths[0])
      throw new ArgumentOutOfRangeException(nameof(x));
    if ((uint)y >= (uint)_levelHeights[0])
      throw new ArgumentOutOfRangeException(nameof(y));

    var cx = x;
    var cy = y;
    for (var level = 0; level < _levels; ++level) {
      indices[level] = cy * _levelWidths[level] + cx;
      cx >>= 1;
      cy >>= 1;
    }
  }

  /// <summary>Builds every parent minimum before any encoder bit is emitted.</summary>
  private void _BuildValues() {
    if (_built)
      return;

    for (var level = 1; level < _levels; ++level) {
      Array.Fill(_values[level], _UNKNOWN);
      var childWidth = _levelWidths[level - 1];
      var childHeight = _levelHeights[level - 1];
      var parentWidth = _levelWidths[level];
      var parentHeight = _levelHeights[level];

      for (var py = 0; py < parentHeight; ++py)
        for (var px = 0; px < parentWidth; ++px) {
          var minimum = _UNKNOWN;
          var firstX = px << 1;
          var firstY = py << 1;

          for (var dy = 0; dy < 2; ++dy) {
            var y = firstY + dy;
            if (y >= childHeight)
              continue;

            for (var dx = 0; dx < 2; ++dx) {
              var x = firstX + dx;
              if (x >= childWidth)
                continue;
              minimum = Math.Min(minimum, _values[level - 1][y * childWidth + x]);
            }
          }

          _values[level][py * parentWidth + px] = minimum;
        }
    }

    _built = true;
  }
}
