using System;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Progressive interlaced decoder for FLIF. Decodes images from coarse to fine
/// resolution using a powers-of-2 scheme (similar to PNG Adam7 but simpler).
///
/// Zoom levels go from the smallest (1x1 or 2x1/1x2) up to the full resolution.
/// At each zoom level, the image resolution doubles in one or both dimensions.
/// Previously decoded pixels serve as context for the next level.
///
/// For a WxH image, the zoom levels are:
///   Level k: ceil(W / 2^k) x ceil(H / 2^k)
/// The coarsest level has max(W,H) reduced to 1 or 2 pixels.
/// </summary>
internal sealed class FlifInterlacedDecoder {

  private readonly FlifRangeDecoder _decoder;
  private readonly FlifManiacForest _forest;
  private readonly int _channelCount;
  private readonly int _minValue;
  private readonly int _maxValue;

  public FlifInterlacedDecoder(FlifRangeDecoder decoder, FlifManiacForest forest, int channelCount, int minValue, int maxValue) {
    _decoder = decoder;
    _forest = forest;
    _channelCount = channelCount;
    _minValue = minValue;
    _maxValue = maxValue;
  }

  /// <summary>
  /// Computes the number of zoom levels for the given dimensions.
  /// </summary>
  public static int ComputeZoomLevels(int width, int height) {
    var maxDim = Math.Max(width, height);
    if (maxDim <= 1)
      return 0;

    var levels = 0;
    var size = 1;
    while (size < maxDim) {
      size <<= 1;
      ++levels;
    }

    return levels;
  }

  /// <summary>
  /// Decodes an interlaced image, returning channel planes at full resolution.
  /// </summary>
  public int[][] DecodeInterlaced(int width, int height) {
    var channels = new int[_channelCount][];
    for (var c = 0; c < _channelCount; ++c)
      channels[c] = new int[width * height];

    var zoomLevels = ComputeZoomLevels(width, height);

    Span<int> emptyProps = stackalloc int[FlifManiacForest.PropertyCount];
    if (zoomLevels == 0) {
      // 1x1 image: decode directly
      for (var c = 0; c < _channelCount; ++c) {
        var tree = _forest.GetTree(c);
        var ctx = tree.Resolve(emptyProps);
        channels[c][0] = ctx.Decode(_decoder, _minValue, _maxValue);
      }

      return channels;
    }

    // Decode from coarsest to finest zoom level
    for (var level = zoomLevels; level >= 0; --level) {
      var stepX = 1 << level;
      var stepY = 1 << level;
      var zoomW = (width + stepX - 1) / stepX;
      var zoomH = (height + stepY - 1) / stepY;

      for (var c = 0; c < _channelCount; ++c)
        _DecodeZoomLevel(channels[c], width, height, stepX, stepY, zoomW, zoomH, level, c);
    }

    return channels;
  }

  private void _DecodeZoomLevel(int[] plane, int fullWidth, int fullHeight, int stepX, int stepY, int zoomW, int zoomH, int level, int channel) {
    var tree = _forest.GetTree(channel);
    Span<int> properties = stackalloc int[FlifManiacForest.PropertyCount];

    for (var zy = 0; zy < zoomH; ++zy) {
      for (var zx = 0; zx < zoomW; ++zx) {
        var x = zx * stepX;
        var y = zy * stepY;

        if (x >= fullWidth || y >= fullHeight)
          continue;

        // Skip pixels already decoded at a coarser level
        if (level < ComputeZoomLevels(fullWidth, fullHeight)) {
          var coarserStepX = stepX << 1;
          var coarserStepY = stepY << 1;
          if (x % coarserStepX == 0 && y % coarserStepY == 0)
            continue; // already decoded
        }

        var idx = y * fullWidth + x;

        // Compute prediction from available neighbors at current and coarser levels
        var prediction = _InterlacedPredict(plane, fullWidth, fullHeight, x, y, stepX, stepY);

        // Compute properties for the pixel
        _ComputeInterlacedProperties(plane, fullWidth, fullHeight, x, y, stepX, stepY, properties);

        var ctx = tree.Resolve(properties);

        var residualMin = _minValue - prediction;
        var residualMax = _maxValue - prediction;
        var residual = ctx.Decode(_decoder, residualMin, residualMax);
        plane[idx] = prediction + residual;
      }
    }
  }

  /// <summary>
  /// Computes the prediction for an interlaced pixel using neighbors from coarser levels.
  /// </summary>
  private static int _InterlacedPredict(int[] plane, int width, int height, int x, int y, int stepX, int stepY) {
    // Use median prediction from neighboring already-decoded pixels
    var left = _GetInterlacedNeighbor(plane, width, height, x - stepX, y);
    var above = _GetInterlacedNeighbor(plane, width, height, x, y - stepY);
    var aboveLeft = _GetInterlacedNeighbor(plane, width, height, x - stepX, y - stepY);
    return FlifManiacForest.MedianPredictor(left, above, aboveLeft);
  }

  private static int _GetInterlacedNeighbor(int[] plane, int width, int height, int x, int y) {
    if (x < 0 || x >= width || y < 0 || y >= height)
      return 0;
    return plane[y * width + x];
  }

  private static void _ComputeInterlacedProperties(int[] plane, int width, int height, int x, int y, int stepX, int stepY, Span<int> properties) {
    var left = _GetInterlacedNeighbor(plane, width, height, x - stepX, y);
    var above = _GetInterlacedNeighbor(plane, width, height, x, y - stepY);
    var aboveLeft = _GetInterlacedNeighbor(plane, width, height, x - stepX, y - stepY);
    var aboveRight = _GetInterlacedNeighbor(plane, width, height, x + stepX, y - stepY);
    var aboveAbove = _GetInterlacedNeighbor(plane, width, height, x, y - 2 * stepY);
    var leftLeft = _GetInterlacedNeighbor(plane, width, height, x - 2 * stepX, y);

    properties[0] = left;
    properties[1] = above;
    properties[2] = aboveLeft;
    properties[3] = aboveRight;
    properties[4] = left - above;
    properties[5] = left + above - aboveLeft;
    properties[6] = aboveAbove;
    properties[7] = leftLeft;
  }
}

/// <summary>
/// Progressive interlaced encoder for FLIF. Counterpart of <see cref="FlifInterlacedDecoder"/>.
/// </summary>
internal sealed class FlifInterlacedEncoder {

  private readonly FlifRangeEncoder _encoder;
  private readonly FlifManiacForest _forest;
  private readonly int _channelCount;
  private readonly int _minValue;
  private readonly int _maxValue;

  public FlifInterlacedEncoder(FlifRangeEncoder encoder, FlifManiacForest forest, int channelCount, int minValue, int maxValue) {
    _encoder = encoder;
    _forest = forest;
    _channelCount = channelCount;
    _minValue = minValue;
    _maxValue = maxValue;
  }

  /// <summary>
  /// Encodes an interlaced image from channel planes.
  /// First builds and writes MANIAC trees, then encodes at each zoom level.
  /// </summary>
  public void EncodeInterlaced(int[][] channels, int width, int height) {
    // Build MANIAC trees from training data (simplified: use leaf-only trees)
    _forest.WriteTrees(_encoder, _minValue, _maxValue);

    var zoomLevels = FlifInterlacedDecoder.ComputeZoomLevels(width, height);

    Span<int> emptyProps = stackalloc int[FlifManiacForest.PropertyCount];
    if (zoomLevels == 0) {
      // 1x1 image
      for (var c = 0; c < _channelCount; ++c) {
        var tree = _forest.GetTree(c);
        var ctx = tree.Resolve(emptyProps);
        ctx.Encode(_encoder, channels[c][0], _minValue, _maxValue);
      }

      return;
    }

    // Encode from coarsest to finest
    for (var level = zoomLevels; level >= 0; --level) {
      var stepX = 1 << level;
      var stepY = 1 << level;
      var zoomW = (width + stepX - 1) / stepX;
      var zoomH = (height + stepY - 1) / stepY;

      for (var c = 0; c < _channelCount; ++c)
        _EncodeZoomLevel(channels[c], width, height, stepX, stepY, zoomW, zoomH, level, c);
    }
  }

  private void _EncodeZoomLevel(int[] plane, int fullWidth, int fullHeight, int stepX, int stepY, int zoomW, int zoomH, int level, int channel) {
    var tree = _forest.GetTree(channel);
    Span<int> properties = stackalloc int[FlifManiacForest.PropertyCount];

    for (var zy = 0; zy < zoomH; ++zy) {
      for (var zx = 0; zx < zoomW; ++zx) {
        var x = zx * stepX;
        var y = zy * stepY;

        if (x >= fullWidth || y >= fullHeight)
          continue;

        // Skip already-encoded pixels from coarser levels
        if (level < FlifInterlacedDecoder.ComputeZoomLevels(fullWidth, fullHeight)) {
          var coarserStepX = stepX << 1;
          var coarserStepY = stepY << 1;
          if (x % coarserStepX == 0 && y % coarserStepY == 0)
            continue;
        }

        var idx = y * fullWidth + x;
        var prediction = _InterlacedPredict(plane, fullWidth, fullHeight, x, y, stepX, stepY);

        _ComputeInterlacedProperties(plane, fullWidth, fullHeight, x, y, stepX, stepY, properties);
        var ctx = tree.Resolve(properties);

        var residual = plane[idx] - prediction;
        var residualMin = _minValue - prediction;
        var residualMax = _maxValue - prediction;
        ctx.Encode(_encoder, residual, residualMin, residualMax);
      }
    }
  }

  private static int _InterlacedPredict(int[] plane, int width, int height, int x, int y, int stepX, int stepY) {
    var left = _GetNeighbor(plane, width, height, x - stepX, y);
    var above = _GetNeighbor(plane, width, height, x, y - stepY);
    var aboveLeft = _GetNeighbor(plane, width, height, x - stepX, y - stepY);
    return FlifManiacForest.MedianPredictor(left, above, aboveLeft);
  }

  private static int _GetNeighbor(int[] plane, int width, int height, int x, int y) {
    if (x < 0 || x >= width || y < 0 || y >= height)
      return 0;
    return plane[y * width + x];
  }

  private static void _ComputeInterlacedProperties(int[] plane, int width, int height, int x, int y, int stepX, int stepY, Span<int> properties) {
    var left = _GetNeighbor(plane, width, height, x - stepX, y);
    var above = _GetNeighbor(plane, width, height, x, y - stepY);
    var aboveLeft = _GetNeighbor(plane, width, height, x - stepX, y - stepY);
    var aboveRight = _GetNeighbor(plane, width, height, x + stepX, y - stepY);
    var aboveAbove = _GetNeighbor(plane, width, height, x, y - 2 * stepY);
    var leftLeft = _GetNeighbor(plane, width, height, x - 2 * stepX, y);

    properties[0] = left;
    properties[1] = above;
    properties[2] = aboveLeft;
    properties[3] = aboveRight;
    properties[4] = left - above;
    properties[5] = left + above - aboveLeft;
    properties[6] = aboveAbove;
    properties[7] = leftLeft;
  }
}
