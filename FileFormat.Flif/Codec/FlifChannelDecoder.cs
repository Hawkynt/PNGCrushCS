using System;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Decodes per-channel pixel data using MANIAC context modeling and the range decoder.
/// Pixels are predicted using spatial neighbors, and the residual (prediction error)
/// is coded with a near-zero integer coder selected by the MANIAC tree.
///
/// For non-interlaced mode, pixels are scanned left-to-right, top-to-bottom.
/// The prediction is the median of (left, above, left+above-above_left).
/// </summary>
internal sealed class FlifChannelDecoder {

  private readonly FlifRangeDecoder _decoder;
  private readonly FlifManiacForest _forest;
  private readonly int _channelCount;
  private readonly int _minValue;
  private readonly int _maxValue;

  public FlifChannelDecoder(FlifRangeDecoder decoder, FlifManiacForest forest, int channelCount, int minValue, int maxValue) {
    _decoder = decoder;
    _forest = forest;
    _channelCount = channelCount;
    _minValue = minValue;
    _maxValue = maxValue;
  }

  /// <summary>
  /// Decodes all channels for a non-interlaced image.
  /// Returns an array of channel planes, each containing width*height integer values.
  /// </summary>
  public int[][] DecodeNonInterlaced(int width, int height) {
    var channels = new int[_channelCount][];
    for (var c = 0; c < _channelCount; ++c)
      channels[c] = new int[width * height];

    // Decode channel by channel
    Span<int> properties = stackalloc int[FlifManiacForest.PropertyCount];
    for (var c = 0; c < _channelCount; ++c) {
      var tree = _forest.GetTree(c);
      var plane = channels[c];

      for (var y = 0; y < height; ++y) {
        for (var x = 0; x < width; ++x) {
          var idx = y * width + x;

          // Compute prediction
          var left = x > 0 ? plane[idx - 1] : 0;
          var above = y > 0 ? plane[idx - width] : 0;
          var aboveLeft = (x > 0 && y > 0) ? plane[idx - width - 1] : 0;
          var prediction = FlifManiacForest.MedianPredictor(left, above, aboveLeft);

          // Compute properties for MANIAC tree traversal
          FlifManiacForest.ComputeProperties(plane, width, height, x, y, properties);

          // Traverse MANIAC tree to get context
          var ctx = tree.Resolve(properties);

          // Compute valid residual range
          var residualMin = _minValue - prediction;
          var residualMax = _maxValue - prediction;

          // Decode residual
          var residual = ctx.Decode(_decoder, residualMin, residualMax);
          plane[idx] = prediction + residual;
        }
      }
    }

    return channels;
  }
}
