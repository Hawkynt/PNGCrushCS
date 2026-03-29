using System;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// JPEG XR image encoder implementing ITU-T T.832.
/// Encodes RGB24 or Gray8 pixels into a JPEG XR compressed bitstream.
/// Pipeline: extract pixels -> color transform (via JxrColorTransform) ->
///   forward LBT (via JxrLbt) -> quantize (via JxrQuantizer) ->
///   entropy encode (via JxrAdaptiveVlcEngine + JxrMacroblockEncoder) -> assemble bitstream.
/// </summary>
internal sealed class JxrEncoder {

  private const int _MACROBLOCK_SIZE = 16;

  /// <summary>Default quantization parameter for DC coefficients (1 = lossless).</summary>
  private const int _DEFAULT_QUANT_DC = 1;

  /// <summary>Default quantization parameter for LP coefficients (1 = lossless).</summary>
  private const int _DEFAULT_QUANT_LP = 1;

  /// <summary>Default quantization parameter for HP coefficients (1 = lossless).</summary>
  private const int _DEFAULT_QUANT_HP = 1;

  /// <summary>Default color transform mode for RGB images.</summary>
  private const JxrColorTransform.Mode _DEFAULT_COLOR_MODE = JxrColorTransform.Mode.YCoCg;

  /// <summary>
  /// Encodes pixel data into a JPEG XR compressed bitstream.
  /// </summary>
  /// <param name="pixelData">Raw pixel data: Gray8 (1 byte/pixel) or RGB24 (3 bytes/pixel).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="componentCount">1 for grayscale, 3 for RGB.</param>
  /// <returns>Compressed bitstream bytes.</returns>
  public static byte[] Encode(byte[] pixelData, int width, int height, int componentCount) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    componentCount = Math.Clamp(componentCount, 1, 3);
    var bands = JxrBandsPresent.All;
    var colorMode = componentCount >= 3 ? _DEFAULT_COLOR_MODE : JxrColorTransform.Mode.Identity;

    var writer = new JxrBitWriter();

    // Write image plane header
    _WritePlaneHeader(writer, componentCount, bands, colorMode);

    // Create codec components
    var quantizer = new JxrQuantizer(_DEFAULT_QUANT_DC, _DEFAULT_QUANT_LP, _DEFAULT_QUANT_HP, componentCount);
    var vlcEngine = new JxrAdaptiveVlcEngine();
    var mbEncoder = new JxrMacroblockEncoder(componentCount, quantizer, vlcEngine);

    var mbWide = (width + _MACROBLOCK_SIZE - 1) / _MACROBLOCK_SIZE;
    var mbHigh = (height + _MACROBLOCK_SIZE - 1) / _MACROBLOCK_SIZE;

    // Allocate per-component macroblock buffers
    var channelBuffers = new int[componentCount][];
    for (var c = 0; c < componentCount; ++c)
      channelBuffers[c] = new int[256];

    for (var mbY = 0; mbY < mbHigh; ++mbY) {
      mbEncoder.ResetRowState();

      for (var mbX = 0; mbX < mbWide; ++mbX) {
        // Extract pixels into channel buffers
        if (componentCount == 3)
          JxrColorTransform.ExtractRgb24(pixelData, width, height, mbX, mbY,
            channelBuffers[0], channelBuffers[1], channelBuffers[2]);
        else
          JxrColorTransform.ExtractGray8(pixelData, width, height, mbX, mbY, channelBuffers[0]);

        // Forward color transform (RGB -> YCoCg or other mode)
        JxrColorTransform.ForwardTransform(colorMode, channelBuffers);

        // Encode the macroblock (forward LBT + quantize + entropy code happens inside)
        mbEncoder.EncodeMacroblock(writer, channelBuffers, bands);
      }
    }

    writer.AlignToByte();
    return writer.ToArray();
  }

  /// <summary>Writes the image plane header to the bitstream.</summary>
  private static void _WritePlaneHeader(JxrBitWriter writer, int componentCount,
    JxrBandsPresent bands, JxrColorTransform.Mode colorMode) {

    // Spatial transform: 0 = no spatial reorientation
    writer.WriteBits(0, 3);

    // Bands present
    writer.WriteBits((uint)bands, 2);

    // Overlap mode: 1 = first level overlap
    writer.WriteBits(1, 2);

    // Color transform mode (2 bits, only for multi-channel)
    if (componentCount >= 3)
      writer.WriteBits((uint)colorMode, 2);

    var hasLp = bands != JxrBandsPresent.DcOnly;
    var hasHp = bands == JxrBandsPresent.All || bands == JxrBandsPresent.NoFlexbits;

    // Quantization parameters (luma)
    writer.WriteBits((uint)_DEFAULT_QUANT_DC, 8);
    if (hasLp)
      writer.WriteBits((uint)_DEFAULT_QUANT_LP, 8);
    if (hasHp)
      writer.WriteBits((uint)_DEFAULT_QUANT_HP, 8);

    // Per-channel QP: 1 = chroma same as luma
    if (componentCount > 1)
      writer.WriteBit(1);

    // Tile structure: 0x0 = single tile (1 tile column, 1 tile row)
    writer.WriteBits(0, 4); // tile columns log2
    writer.WriteBits(0, 4); // tile rows log2

    writer.AlignToByte();
  }
}
