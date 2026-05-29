using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// JPEG XL modular mode decoder. Decodes pixel data from the codestream using
/// the modular sub-codec pipeline:
/// 1. Read global MA (Meta-Adaptive) tree for context modeling
/// 2. Read transform chain (squeeze, palette, etc.)
/// 3. For each channel, decode residuals using entropy decoder with predictor context
/// 4. Apply inverse transforms in reverse order
/// 5. Return decoded channel data
/// </summary>
internal static class JxlModularDecoder {

  /// <summary>
  /// Decode a modular image from the bitstream.
  /// Returns one int array per channel, each containing width*height pixel values.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the start of modular data.</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="numChannels">Number of channels to decode.</param>
  /// <param name="bitDepth">Bit depth per sample (typically 8).</param>
  /// <returns>Array of channel data, each channel is a flat int array of width*height.</returns>
  public static int[][] Decode(JxlBitReader reader, int width, int height, int numChannels, int bitDepth) {
    ArgumentNullException.ThrowIfNull(reader);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");
    if (numChannels <= 0)
      throw new ArgumentOutOfRangeException(nameof(numChannels));

    var maxVal = (1 << bitDepth) - 1;

    // Read transform chain
    var transforms = _ReadTransforms(reader);

    // Compute channel dimensions after transforms
    var channelWidths = new int[numChannels];
    var channelHeights = new int[numChannels];
    for (var c = 0; c < numChannels; ++c) {
      channelWidths[c] = width;
      channelHeights[c] = height;
    }

    // Apply forward transforms to compute sub-channel dimensions
    var squeezeDepth = 0;
    foreach (var t in transforms)
      if (t == TransformType.Squeeze)
        ++squeezeDepth;

    // Total number of sub-channels after squeeze
    var totalSubChannels = numChannels;
    for (var i = 0; i < squeezeDepth; ++i)
      totalSubChannels += numChannels; // each squeeze level adds difference sub-channels

    // Read the MA tree (context model)
    var useGlobalTree = reader.ReadBool();
    var predictorMode = useGlobalTree ? (int)reader.ReadBits(4) : 5; // default: gradient
    if (predictorMode >= JxlPredictor.ModeCount)
      predictorMode = 5;

    // Number of contexts for entropy coding
    // Simplified: one context per channel, plus predictor-error contexts
    var numContexts = Math.Max(1, numChannels * 3);

    // Read entropy-coded data
    var channels = new int[numChannels][];

    // Check encoding mode
    var useDirectCoding = reader.ReadBool();

    if (useDirectCoding) {
      // Direct bit coding: each residual is encoded as a fixed-width signed integer
      var residualBits = (int)reader.ReadBits(4) + 1;

      for (var c = 0; c < numChannels; ++c)
        channels[c] = _DecodeChannelDirect(reader, channelWidths[c], channelHeights[c], predictorMode, maxVal, residualBits);
    } else {
      // Entropy-coded residuals
      var entropy = JxlEntropyDecoder.Read(reader, numContexts);

      for (var c = 0; c < numChannels; ++c)
        channels[c] = _DecodeChannelEntropy(entropy, channelWidths[c], channelHeights[c], c, predictorMode, maxVal);
    }

    // Apply inverse transforms in reverse order
    for (var i = transforms.Count - 1; i >= 0; --i)
      if (transforms[i] == TransformType.Squeeze)
        for (var c = 0; c < numChannels; ++c)
          _ApplyInverseSqueeze(channels, c, channelWidths[c], channelHeights[c]);

    return channels;
  }

  /// <summary>
  /// Decode a single channel using direct bit coding with prediction.
  /// Each pixel is stored as: residual = actual - predicted, zigzag-encoded.
  /// </summary>
  private static int[] _DecodeChannelDirect(
    JxlBitReader reader, int width, int height, int predictorMode, int maxVal, int residualBits
  ) {
    var pixels = new int[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var n = y > 0 ? pixels[(y - 1) * width + x] : 0;
        var w = x > 0 ? pixels[y * width + x - 1] : 0;
        var nw = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : 0;
        var ne = x < width - 1 && y > 0 ? pixels[(y - 1) * width + x + 1] : n;
        var nn = y > 1 ? pixels[(y - 2) * width + x] : n;
        var ww = x > 1 ? pixels[y * width + x - 2] : w;

        var predicted = JxlPredictor.Predict(predictorMode, n, w, nw, ne, nn, ww, maxVal);

        // Read zigzag-encoded residual
        var encoded = reader.ReadBits(residualBits);
        var residual = _ZigzagDecode(encoded);

        var pixel = Math.Clamp(predicted + residual, 0, maxVal);
        pixels[y * width + x] = pixel;
      }

    return pixels;
  }

  /// <summary>
  /// Decode a single channel using entropy-coded residuals with prediction.
  /// </summary>
  private static int[] _DecodeChannelEntropy(
    JxlEntropyDecoder entropy, int width, int height, int channelIndex, int predictorMode, int maxVal
  ) {
    var pixels = new int[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var n = y > 0 ? pixels[(y - 1) * width + x] : 0;
        var w = x > 0 ? pixels[y * width + x - 1] : 0;
        var nw = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : 0;
        var ne = x < width - 1 && y > 0 ? pixels[(y - 1) * width + x + 1] : n;
        var nn = y > 1 ? pixels[(y - 2) * width + x] : n;
        var ww = x > 1 ? pixels[y * width + x - 2] : w;

        var predicted = JxlPredictor.Predict(predictorMode, n, w, nw, ne, nn, ww, maxVal);

        // Context selection: based on channel and gradient magnitude
        var gradMag = Math.Abs(n - nw) + Math.Abs(w - nw);
        var context = channelIndex * 3 + Math.Min(gradMag / (maxVal / 4 + 1), 2);
        context = Math.Max(0, context);

        var token = entropy.ReadInt(context);
        var residual = _ZigzagDecode((uint)token);

        var pixel = Math.Clamp(predicted + residual, 0, maxVal);
        pixels[y * width + x] = pixel;
      }

    return pixels;
  }

  private static List<TransformType> _ReadTransforms(JxlBitReader reader) {
    var transforms = new List<TransformType>();

    // Read number of transforms
    var hasTransforms = reader.ReadBool();
    if (!hasTransforms)
      return transforms;

    var count = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 1, 4);
    for (var i = 0; i < count; ++i) {
      var type = (TransformType)reader.ReadBits(2);
      transforms.Add(type);

      // Read transform parameters (simplified)
      if (type == TransformType.Squeeze) {
        // Squeeze parameters: number of levels
        var _levels = reader.ReadBits(3) + 1;
      }
    }

    return transforms;
  }

  private static void _ApplyInverseSqueeze(int[][] channels, int channelIndex, int width, int height) {
    var flat = channels[channelIndex];
    var chan = new int[height, width];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        chan[y, x] = flat[y * width + x];

    JxlSqueezeTransform.InverseSqueeze(chan, width, height);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        flat[y * width + x] = chan[y, x];
  }

  /// <summary>Decode a zigzag-encoded unsigned value to signed: 0->0, 1->-1, 2->1, 3->-2, etc.</summary>
  private static int _ZigzagDecode(uint value) =>
    (int)(value >> 1) ^ -(int)(value & 1);

  /// <summary>Encode a signed value to zigzag unsigned: 0->0, -1->1, 1->2, -2->3, etc.</summary>
  internal static uint ZigzagEncode(int value) =>
    (uint)((value << 1) ^ (value >> 31));

  private enum TransformType {
    Rct = 0,     // Reversible Color Transform
    Palette = 1, // Palette transform
    Squeeze = 2, // Multi-resolution squeeze
    Reserved = 3,
  }
}
