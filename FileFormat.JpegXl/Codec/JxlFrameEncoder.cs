using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Frame-level pipeline orchestrator for JPEG XL encoding.
/// Writes the frame header and delegates to the modular encoder
/// to produce lossless compressed frame data.
/// </summary>
internal static class JxlFrameEncoder {

  /// <summary>
  /// Encode a single frame as modular (lossless) data.
  /// </summary>
  /// <param name="pixelData">Input pixel data (interleaved channel layout).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="numChannels">Number of color channels (1 for gray, 3 for RGB).</param>
  /// <param name="bitDepth">Bit depth per sample.</param>
  /// <returns>Encoded frame data as byte array.</returns>
  public static byte[] EncodeFrame(byte[] pixelData, int width, int height, int numChannels, int bitDepth) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));

    var writer = new JxlBitWriter(pixelData.Length);

    // Write frame header
    _WriteFrameHeader(writer);

    // Convert interleaved bytes to int channels
    var channels = _BytesToChannels(pixelData, width, height, numChannels);

    // Encode via modular sub-codec
    JxlModularEncoder.Encode(writer, channels, width, height, bitDepth);

    writer.ZeroPadToByte();
    return writer.ToArray();
  }

  /// <summary>
  /// Write a minimal modular frame header.
  /// </summary>
  private static void _WriteFrameHeader(JxlBitWriter writer) {
    // all_default = true (use default modular frame settings)
    writer.WriteBool(true);
  }

  /// <summary>
  /// Convert interleaved byte pixel data to separate int channel arrays.
  /// </summary>
  private static int[][] _BytesToChannels(byte[] pixelData, int width, int height, int numChannels) {
    var pixels = width * height;
    var channels = new int[numChannels][];

    for (var c = 0; c < numChannels; ++c)
      channels[c] = new int[pixels];

    for (var i = 0; i < pixels; ++i)
      for (var c = 0; c < numChannels; ++c) {
        var srcIndex = i * numChannels + c;
        channels[c][i] = srcIndex < pixelData.Length ? pixelData[srcIndex] : 0;
      }

    return channels;
  }
}
