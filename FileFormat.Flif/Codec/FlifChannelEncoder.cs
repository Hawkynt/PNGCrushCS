using System;
using System.Collections.Generic;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Encodes per-channel pixel data using MANIAC context modeling and the range encoder.
/// This is the encoding counterpart of <see cref="FlifChannelDecoder"/>.
///
/// The encoder first makes a training pass to build MANIAC trees,
/// then encodes the pixel data using those trees.
/// </summary>
internal sealed class FlifChannelEncoder {

  private readonly FlifRangeEncoder _encoder;
  private readonly FlifManiacForest _forest;
  private readonly int _channelCount;
  private readonly int _minValue;
  private readonly int _maxValue;

  public FlifChannelEncoder(FlifRangeEncoder encoder, FlifManiacForest forest, int channelCount, int minValue, int maxValue) {
    _encoder = encoder;
    _forest = forest;
    _channelCount = channelCount;
    _minValue = minValue;
    _maxValue = maxValue;
  }

  /// <summary>
  /// Encodes all channels for a non-interlaced image.
  /// First builds MANIAC trees from the data, writes them, then encodes pixels.
  /// </summary>
  public void EncodeNonInterlaced(int[][] channels, int width, int height) {
    // Phase 1: Build MANIAC trees from training data
    _BuildAndWriteTrees(channels, width, height);

    // Phase 2: Encode pixel data
    Span<int> properties = stackalloc int[FlifManiacForest.PropertyCount];
    for (var c = 0; c < _channelCount; ++c) {
      var tree = _forest.GetTree(c);
      var plane = channels[c];

      for (var y = 0; y < height; ++y) {
        for (var x = 0; x < width; ++x) {
          var idx = y * width + x;

          // Compute prediction (must match decoder)
          var left = x > 0 ? plane[idx - 1] : 0;
          var above = y > 0 ? plane[idx - width] : 0;
          var aboveLeft = (x > 0 && y > 0) ? plane[idx - width - 1] : 0;
          var prediction = FlifManiacForest.MedianPredictor(left, above, aboveLeft);

          // Compute properties
          FlifManiacForest.ComputeProperties(plane, width, height, x, y, properties);

          // Traverse tree to get context
          var ctx = tree.Resolve(properties);

          // Compute residual and encode
          var residual = plane[idx] - prediction;
          var residualMin = _minValue - prediction;
          var residualMax = _maxValue - prediction;

          ctx.Encode(_encoder, residual, residualMin, residualMax);
        }
      }
    }
  }

  private void _BuildAndWriteTrees(int[][] channels, int width, int height) {
    for (var c = 0; c < _channelCount; ++c) {
      var builder = new FlifManiacTreeBuilder(_minValue, _maxValue);
      var plane = channels[c];
      var properties = new int[FlifManiacForest.PropertyCount];

      // Sample the data for tree building (use all pixels for small images,
      // or subsample for large ones)
      var totalPixels = width * height;
      var sampleStep = totalPixels > 10000 ? totalPixels / 5000 : 1;
      var sampleIdx = 0;

      for (var y = 0; y < height; ++y) {
        for (var x = 0; x < width; ++x) {
          ++sampleIdx;
          if (sampleIdx % sampleStep != 0)
            continue;

          var idx = y * width + x;
          var left = x > 0 ? plane[idx - 1] : 0;
          var above = y > 0 ? plane[idx - width] : 0;
          var aboveLeft = (x > 0 && y > 0) ? plane[idx - width - 1] : 0;
          var prediction = FlifManiacForest.MedianPredictor(left, above, aboveLeft);

          FlifManiacForest.ComputeProperties(plane, width, height, x, y, properties.AsSpan());
          var residual = plane[idx] - prediction;

          builder.AddSample((int[])properties.Clone(), residual);
        }
      }

      // Build tree from samples and store it in the forest
      var tree = builder.Build();

      // Replace the tree in the forest using reflection-free approach:
      // We write the tree structure and the decoder will read it back.
      // For encoding, we set the tree directly.
      _SetTree(c, tree);
    }

    // Write the MANIAC trees to the bitstream
    _forest.WriteTrees(_encoder, _minValue, _maxValue);
  }

  /// <summary>Sets the MANIAC tree for a channel (used during encoding).</summary>
  private void _SetTree(int channel, FlifManiacNode tree) {
    // The forest stores trees internally; we need to copy the built tree
    // into the forest's tree for this channel.
    var forestTree = _forest.GetTree(channel);
    _CopyTree(tree, forestTree);
  }

  private static void _CopyTree(FlifManiacNode source, FlifManiacNode target) {
    target.Property = source.Property;
    target.SplitValue = source.SplitValue;

    if (source.IsLeaf) {
      target.Left = null;
      target.Right = null;
      return;
    }

    target.Left = new FlifManiacNode();
    target.Right = new FlifManiacNode();
    _CopyTree(source.Left!, target.Left);
    _CopyTree(source.Right!, target.Right);
  }
}
