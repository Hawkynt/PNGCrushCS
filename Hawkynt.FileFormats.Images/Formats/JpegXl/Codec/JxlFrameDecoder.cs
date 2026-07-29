using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Frame-level pipeline orchestrator for JPEG XL decoding.
/// Reads the frame header, delegates to the appropriate sub-codec
/// (modular or VarDCT), and produces output pixel data.
/// </summary>
internal static class JxlFrameDecoder {

  /// <summary>Modular frame encoding (lossless).</summary>
  private const int _EncodingModular = 0;

  /// <summary>VarDCT frame encoding (lossy).</summary>
  private const int _EncodingVarDct = 1;

  /// <summary>
  /// Decode a single frame from codestream data.
  /// </summary>
  /// <param name="codestream">Raw codestream bytes (after signature and SizeHeader).</param>
  /// <param name="offset">Offset into the codestream where frame data begins.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="numChannels">Number of color channels (1 for gray, 3 for RGB).</param>
  /// <param name="bitDepth">Bit depth per sample.</param>
  /// <returns>Decoded pixel data as byte array (interleaved channel layout).</returns>
  public static byte[] DecodeFrame(byte[] codestream, int offset, int width, int height, int numChannels, int bitDepth) {
    ArgumentNullException.ThrowIfNull(codestream);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));

    var reader = new JxlBitReader(codestream, offset);

    // Read frame header
    var encoding = _ReadFrameHeader(reader);

    if (encoding != _EncodingModular)
      throw new NotSupportedException("Only modular (lossless) encoding is supported.");

    // Decode modular sub-codec
    var channels = JxlModularDecoder.Decode(reader, width, height, numChannels, bitDepth);

    // Convert int channels to interleaved byte output
    return _ChannelsToBytes(channels, width, height, numChannels, bitDepth);
  }

  /// <summary>
  /// Read the frame header and return the encoding type.
  /// </summary>
  private static int _ReadFrameHeader(JxlBitReader reader) {
    // Frame header starts with: all_default flag
    var allDefault = reader.ReadBool();

    if (allDefault)
      return _EncodingModular; // default frame is modular

    // Frame type (2 bits): 0=regular, 1=LF, 2=reference only, 3=skip progressive
    var _frameType = reader.ReadBits(2);

    // Encoding: 0=modular, 1=VarDCT
    var encoding = (int)reader.ReadBits(1);

    // For modular frames, skip remaining header fields
    if (encoding == _EncodingModular) {
      // Read flags
      var _flags = reader.ReadBits(2);

      // YCbCr flag (0=no transform)
      if (encoding == _EncodingVarDct)
        reader.ReadBits(1);
    }

    // Zero-pad to byte boundary after frame header
    reader.ZeroPadToByte();

    return encoding;
  }

  /// <summary>
  /// Convert decoded int channels to interleaved byte layout.
  /// Handles bit depth conversion and channel interleaving.
  /// </summary>
  private static byte[] _ChannelsToBytes(int[][] channels, int width, int height, int numChannels, int bitDepth) {
    var maxVal = (1 << bitDepth) - 1;
    var pixels = width * height;
    var result = new byte[pixels * numChannels];

    for (var i = 0; i < pixels; ++i)
      for (var c = 0; c < numChannels; ++c) {
        var val = channels[c][i];
        // Clamp and scale to 8-bit if needed
        if (bitDepth <= 8)
          result[i * numChannels + c] = (byte)Math.Clamp(val, 0, maxVal);
        else
          result[i * numChannels + c] = (byte)Math.Clamp(val * 255 / maxVal, 0, 255);
      }

    return result;
  }
}
