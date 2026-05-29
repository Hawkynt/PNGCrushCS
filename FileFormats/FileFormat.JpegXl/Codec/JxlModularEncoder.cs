using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// JPEG XL modular mode encoder. Encodes pixel data into the codestream using
/// the modular sub-codec pipeline:
/// 1. Apply forward transforms (squeeze)
/// 2. For each channel, compute prediction residuals
/// 3. Encode residuals using direct bit coding or entropy coding
/// 4. Write to bitstream
/// </summary>
internal static class JxlModularEncoder {

  /// <summary>
  /// Encode a modular image to the bit writer.
  /// </summary>
  /// <param name="writer">Bit writer to write the encoded data to.</param>
  /// <param name="channels">Array of channel data, each channel is a flat int array of width*height.</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="bitDepth">Bit depth per sample (typically 8).</param>
  public static void Encode(JxlBitWriter writer, int[][] channels, int width, int height, int bitDepth) {
    ArgumentNullException.ThrowIfNull(writer);
    ArgumentNullException.ThrowIfNull(channels);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");

    var maxVal = (1 << bitDepth) - 1;
    var numChannels = channels.Length;

    // Select best predictor mode based on data analysis
    var predictorMode = _AnalyzeBestPredictor(channels, width, height, maxVal);

    // No transforms for now (simplest encoding)
    writer.WriteBool(false); // hasTransforms = false

    // Write MA tree / predictor mode
    writer.WriteBool(true);  // useGlobalTree = true
    writer.WriteBits((uint)predictorMode, 4);

    // Use direct coding (simpler, no entropy tables needed)
    writer.WriteBool(true); // useDirectCoding = true

    // Determine minimum residual bits needed
    var residualBits = _ComputeResidualBits(channels, width, height, predictorMode, maxVal);
    writer.WriteBits((uint)(residualBits - 1), 4);

    // Encode each channel
    for (var c = 0; c < numChannels; ++c)
      _EncodeChannelDirect(writer, channels[c], width, height, predictorMode, maxVal, residualBits);
  }

  /// <summary>
  /// Encode a single channel using direct bit coding with prediction.
  /// </summary>
  private static void _EncodeChannelDirect(
    JxlBitWriter writer, int[] pixels, int width, int height, int predictorMode, int maxVal, int residualBits
  ) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var n = y > 0 ? pixels[(y - 1) * width + x] : 0;
        var w = x > 0 ? pixels[y * width + x - 1] : 0;
        var nw = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : 0;
        var ne = x < width - 1 && y > 0 ? pixels[(y - 1) * width + x + 1] : n;
        var nn = y > 1 ? pixels[(y - 2) * width + x] : n;
        var ww = x > 1 ? pixels[y * width + x - 2] : w;

        var predicted = JxlPredictor.Predict(predictorMode, n, w, nw, ne, nn, ww, maxVal);
        var actual = pixels[y * width + x];
        var residual = actual - predicted;

        // Zigzag encode the residual
        var encoded = JxlModularDecoder.ZigzagEncode(residual);
        writer.WriteBits(encoded, residualBits);
      }
  }

  /// <summary>
  /// Compute the minimum number of bits needed to represent all residuals.
  /// </summary>
  private static int _ComputeResidualBits(int[][] channels, int width, int height, int predictorMode, int maxVal) {
    var maxEncoded = 0u;

    foreach (var pixels in channels)
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var n = y > 0 ? pixels[(y - 1) * width + x] : 0;
          var w = x > 0 ? pixels[y * width + x - 1] : 0;
          var nw = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : 0;
          var ne = x < width - 1 && y > 0 ? pixels[(y - 1) * width + x + 1] : n;
          var nn = y > 1 ? pixels[(y - 2) * width + x] : n;
          var ww = x > 1 ? pixels[y * width + x - 2] : w;

          var predicted = JxlPredictor.Predict(predictorMode, n, w, nw, ne, nn, ww, maxVal);
          var residual = pixels[y * width + x] - predicted;
          var encoded = JxlModularDecoder.ZigzagEncode(residual);

          if (encoded > maxEncoded)
            maxEncoded = encoded;
        }

    // Minimum bits to represent the largest encoded value
    if (maxEncoded == 0)
      return 1;

    var bits = 0;
    var v = maxEncoded;
    while (v > 0) {
      ++bits;
      v >>= 1;
    }
    return Math.Max(1, Math.Min(bits, 16));
  }

  /// <summary>
  /// Analyze the image data to select the best predictor mode.
  /// Tries all 14 modes on a sample and picks the one with lowest total absolute error.
  /// </summary>
  private static int _AnalyzeBestPredictor(int[][] channels, int width, int height, int maxVal) {
    if (width <= 1 && height <= 1)
      return 0; // Zero predictor for single pixel

    var bestMode = 5; // Default: gradient
    var bestError = long.MaxValue;

    // Sample at most 1000 pixels for analysis
    var sampleStep = Math.Max(1, (width * height) / 1000);

    for (var mode = 0; mode < JxlPredictor.ModeCount; ++mode) {
      long totalError = 0;

      foreach (var pixels in channels) {
        var idx = 0;
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x) {
            if (idx % sampleStep == 0) {
              var n = y > 0 ? pixels[(y - 1) * width + x] : 0;
              var w = x > 0 ? pixels[y * width + x - 1] : 0;
              var nw = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : 0;
              var ne = x < width - 1 && y > 0 ? pixels[(y - 1) * width + x + 1] : n;
              var nn = y > 1 ? pixels[(y - 2) * width + x] : n;
              var ww = x > 1 ? pixels[y * width + x - 2] : w;

              var predicted = JxlPredictor.Predict(mode, n, w, nw, ne, nn, ww, maxVal);
              totalError += Math.Abs(pixels[y * width + x] - predicted);
            }
            ++idx;
          }
      }

      if (totalError < bestError) {
        bestError = totalError;
        bestMode = mode;
      }
    }

    return bestMode;
  }
}
