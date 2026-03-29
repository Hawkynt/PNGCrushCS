using System;
using System.Collections.Generic;

namespace FileFormat.Flif.Codec;

/// <summary>
/// MANIAC (Meta-Adaptive Near-zero Integer Arithmetic Coding) decision tree.
/// Each internal node splits on a property of the pixel context (e.g., pixel-left,
/// pixel-above, etc.) at a threshold value. Leaf nodes contain a
/// <see cref="FlifNearZeroContext"/> for entropy coding.
///
/// The tree is built during encoding by accumulating statistics and splitting nodes
/// that exceed a threshold. During decoding, the tree structure is read from the
/// bitstream.
/// </summary>
internal sealed class FlifManiacNode {

  /// <summary>Property index to split on (-1 for leaf nodes).</summary>
  public int Property { get; set; } = -1;

  /// <summary>Split value: left child for property &lt;= value, right child for property &gt; value.</summary>
  public int SplitValue { get; set; }

  /// <summary>Left child (property &lt;= split value), or null for leaf.</summary>
  public FlifManiacNode? Left { get; set; }

  /// <summary>Right child (property &gt; split value), or null for leaf.</summary>
  public FlifManiacNode? Right { get; set; }

  /// <summary>Near-zero integer coder context for leaf nodes.</summary>
  public FlifNearZeroContext Context { get; set; } = new();

  /// <summary>Whether this node is a leaf (no children).</summary>
  public bool IsLeaf => Property < 0;

  /// <summary>
  /// Traverses the tree given pixel properties and returns the leaf node's context.
  /// </summary>
  public FlifNearZeroContext Resolve(ReadOnlySpan<int> properties) {
    var node = this;
    while (!node.IsLeaf) {
      var propValue = node.Property < properties.Length ? properties[node.Property] : 0;
      node = propValue <= node.SplitValue ? node.Left! : node.Right!;
    }

    return node.Context;
  }
}

/// <summary>
/// Manages a collection of MANIAC trees, one per channel.
/// Provides methods to read/write tree structure from/to the range coder.
/// </summary>
internal sealed class FlifManiacForest {

  /// <summary>Number of spatial/gradient properties used for context modeling.</summary>
  internal const int PropertyCount = 8;

  /// <summary>The MANIAC trees, one per channel.</summary>
  private readonly FlifManiacNode[] _trees;

  /// <summary>Number of channels.</summary>
  public int ChannelCount => _trees.Length;

  public FlifManiacForest(int channelCount) {
    _trees = new FlifManiacNode[channelCount];
    for (var i = 0; i < channelCount; ++i)
      _trees[i] = new FlifManiacNode();
  }

  /// <summary>Gets the tree for the given channel.</summary>
  public FlifManiacNode GetTree(int channel) => _trees[channel];

  /// <summary>
  /// Reads the MANIAC tree structure for all channels from the range decoder.
  /// Tree structure: each node is encoded as:
  ///   - property index (0 = leaf, 1..N = split on property index-1)
  ///   - if not leaf: split value, then recursively left and right children
  /// </summary>
  public void ReadTrees(FlifRangeDecoder decoder, int minValue, int maxValue) {
    for (var c = 0; c < _trees.Length; ++c)
      _trees[c] = _ReadNode(decoder, 0, minValue, maxValue);
  }

  /// <summary>
  /// Writes the MANIAC tree structure for all channels to the range encoder.
  /// </summary>
  public void WriteTrees(FlifRangeEncoder encoder, int minValue, int maxValue) {
    for (var c = 0; c < _trees.Length; ++c)
      _WriteNode(encoder, _trees[c], minValue, maxValue);
  }

  /// <summary>
  /// Computes the spatial properties for context modeling at position (x, y) in the given channel plane.
  /// Properties: 0=pixel-left, 1=pixel-above, 2=pixel-above-left, 3=pixel-above-right,
  /// 4=left-above difference, 5=gradient (left+above-above_left), 6=above-above, 7=left-left.
  /// </summary>
  public static void ComputeProperties(
    int[] channelPlane,
    int width,
    int height,
    int x,
    int y,
    Span<int> properties
  ) {
    var idx = y * width + x;
    var current = channelPlane[idx];

    var left = x > 0 ? channelPlane[idx - 1] : 0;
    var above = y > 0 ? channelPlane[idx - width] : 0;
    var aboveLeft = (x > 0 && y > 0) ? channelPlane[idx - width - 1] : 0;
    var aboveRight = (x < width - 1 && y > 0) ? channelPlane[idx - width + 1] : 0;
    var aboveAbove = y > 1 ? channelPlane[idx - 2 * width] : above;
    var leftLeft = x > 1 ? channelPlane[idx - 2] : left;

    properties[0] = left;
    properties[1] = above;
    properties[2] = aboveLeft;
    properties[3] = aboveRight;
    properties[4] = left - above;
    properties[5] = left + above - aboveLeft; // Paeth-like gradient predictor
    properties[6] = aboveAbove;
    properties[7] = leftLeft;
  }

  /// <summary>
  /// Computes the median predictor from left, above, and gradient.
  /// This is the default FLIF predictor for non-interlaced mode.
  /// </summary>
  public static int MedianPredictor(int left, int above, int aboveLeft) {
    var gradient = left + above - aboveLeft;
    // Median of left, above, gradient
    if (left <= above) {
      if (above <= gradient)
        return above;
      return left <= gradient ? gradient : left;
    }

    if (left <= gradient)
      return left;
    return above <= gradient ? gradient : above;
  }

  /// <summary>
  /// Computes the range of possible split values for a given property index.
  /// Properties 0-3 and 6-7 are raw pixel values: [minValue, maxValue].
  /// Property 4 is left-above: [minValue - maxValue, maxValue - minValue].
  /// Property 5 is gradient (left+above-aboveLeft): [2*minValue - maxValue, 2*maxValue - minValue].
  /// </summary>
  private static (int SplitMin, int SplitMax) _PropertyRange(int propertyIndex, int minValue, int maxValue) {
    return propertyIndex switch {
      4 => (minValue - maxValue, maxValue - minValue),
      5 => (2 * minValue - maxValue, 2 * maxValue - minValue),
      _ => (minValue, maxValue),
    };
  }

  private static FlifManiacNode _ReadNode(FlifRangeDecoder decoder, int depth, int minValue, int maxValue) {
    // Limit tree depth to prevent stack overflow
    if (depth > 20)
      return new FlifManiacNode();

    // Read property index: 0 means leaf
    var property = decoder.DecodeUniform(PropertyCount);

    if (property == 0)
      return new FlifManiacNode();

    // Internal node: read split value using property-specific range
    var propIdx = property - 1;
    var (splitMin, splitMax) = _PropertyRange(propIdx, minValue, maxValue);
    var splitValue = splitMin + decoder.DecodeUniform(splitMax - splitMin);

    var node = new FlifManiacNode {
      Property = propIdx,
      SplitValue = splitValue,
      Left = _ReadNode(decoder, depth + 1, minValue, maxValue),
      Right = _ReadNode(decoder, depth + 1, minValue, maxValue),
    };

    return node;
  }

  private static void _WriteNode(FlifRangeEncoder encoder, FlifManiacNode node, int minValue, int maxValue) {
    if (node.IsLeaf) {
      // Leaf: write property index 0
      encoder.EncodeUniform(0, PropertyCount);
      return;
    }

    // Internal node: write property index (1-based) and split value using property-specific range
    encoder.EncodeUniform(node.Property + 1, PropertyCount);
    var (splitMin, splitMax) = _PropertyRange(node.Property, minValue, maxValue);
    // Clamp split value to valid range in case tree builder produced out-of-range values
    var clampedSplit = Math.Clamp(node.SplitValue, splitMin, splitMax);
    encoder.EncodeUniform(clampedSplit - splitMin, splitMax - splitMin);
    _WriteNode(encoder, node.Left!, minValue, maxValue);
    _WriteNode(encoder, node.Right!, minValue, maxValue);
  }
}

/// <summary>
/// Builds a MANIAC tree from training data by greedily splitting nodes
/// to minimize coding cost. Used during encoding.
/// </summary>
internal sealed class FlifManiacTreeBuilder {

  /// <summary>Minimum samples in a node before it can be split.</summary>
  private const int _MIN_SAMPLES_TO_SPLIT = 64;

  /// <summary>Minimum improvement (in estimated bits) to justify a split.</summary>
  private const int _MIN_IMPROVEMENT = 16;

  private readonly int _minValue;
  private readonly int _maxValue;
  private readonly List<(int[] Properties, int Value)> _samples = [];

  public FlifManiacTreeBuilder(int minValue, int maxValue) {
    _minValue = minValue;
    _maxValue = maxValue;
  }

  /// <summary>Adds a training sample.</summary>
  public void AddSample(int[] properties, int value) {
    _samples.Add((properties, value));
  }

  /// <summary>Builds the MANIAC tree from accumulated samples.</summary>
  public FlifManiacNode Build() {
    if (_samples.Count < _MIN_SAMPLES_TO_SPLIT)
      return new FlifManiacNode();

    return _BuildNode(_samples, 0);
  }

  private FlifManiacNode _BuildNode(List<(int[] Properties, int Value)> samples, int depth) {
    if (samples.Count < _MIN_SAMPLES_TO_SPLIT || depth > 15)
      return new FlifManiacNode();

    var baseCost = _EstimateCost(samples);
    var bestProperty = -1;
    var bestSplit = 0;
    var bestImprovement = (double)_MIN_IMPROVEMENT;

    for (var p = 0; p < FlifManiacForest.PropertyCount; ++p) {
      // Find min and max property values
      var propMin = int.MaxValue;
      var propMax = int.MinValue;
      foreach (var (props, _) in samples) {
        if (p < props.Length) {
          propMin = Math.Min(propMin, props[p]);
          propMax = Math.Max(propMax, props[p]);
        }
      }

      if (propMin >= propMax)
        continue;

      // Try a few candidate split values
      var step = Math.Max(1, (propMax - propMin) / 8);
      for (var splitVal = propMin; splitVal < propMax; splitVal += step) {
        var leftSamples = new List<(int[] Properties, int Value)>();
        var rightSamples = new List<(int[] Properties, int Value)>();

        foreach (var sample in samples) {
          var propVal = p < sample.Properties.Length ? sample.Properties[p] : 0;
          if (propVal <= splitVal)
            leftSamples.Add(sample);
          else
            rightSamples.Add(sample);
        }

        if (leftSamples.Count < _MIN_SAMPLES_TO_SPLIT / 4 || rightSamples.Count < _MIN_SAMPLES_TO_SPLIT / 4)
          continue;

        var splitCost = _EstimateCost(leftSamples) + _EstimateCost(rightSamples);
        var improvement = baseCost - splitCost;

        if (improvement > bestImprovement) {
          bestImprovement = improvement;
          bestProperty = p;
          bestSplit = splitVal;
        }
      }
    }

    if (bestProperty < 0)
      return new FlifManiacNode();

    // Split
    var left = new List<(int[] Properties, int Value)>();
    var right = new List<(int[] Properties, int Value)>();
    foreach (var sample in samples) {
      var propVal = bestProperty < sample.Properties.Length ? sample.Properties[bestProperty] : 0;
      if (propVal <= bestSplit)
        left.Add(sample);
      else
        right.Add(sample);
    }

    return new FlifManiacNode {
      Property = bestProperty,
      SplitValue = bestSplit,
      Left = _BuildNode(left, depth + 1),
      Right = _BuildNode(right, depth + 1),
    };
  }

  /// <summary>Estimates the coding cost of a set of samples using variance as proxy.</summary>
  private static double _EstimateCost(List<(int[] Properties, int Value)> samples) {
    if (samples.Count == 0)
      return 0;

    var sum = 0L;
    var sumSq = 0L;
    foreach (var (_, value) in samples) {
      sum += value;
      sumSq += (long)value * value;
    }

    var n = samples.Count;
    var mean = (double)sum / n;
    var variance = (double)sumSq / n - mean * mean;

    // Entropy estimate: log2(sqrt(2*pi*e*variance)) * n
    // Simplified: n * 0.5 * log2(variance + 1)
    return n * 0.5 * Math.Log2(variance + 1);
  }
}
