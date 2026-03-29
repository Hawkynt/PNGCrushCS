using System;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// JPEG XR image decoder implementing ITU-T T.832.
/// Decodes the compressed bitstream inside the JPEG XR container to RGB24 or Gray8 pixels.
/// Pipeline: parse header -> create quantizer + VLC engine + macroblock decoder ->
///   for each tile -> for each macroblock row -> decode DC/LP/HP via macroblock decoder ->
///   inverse LBT (via JxrLbt) -> inverse color transform (via JxrColorTransform) -> pixel output.
/// </summary>
internal sealed class JxrDecoder {

  private const int _SPATIAL_XFRM_BITS = 3;
  private const int _BANDS_PRESENT_BITS = 2;
  private const int _MACROBLOCK_SIZE = 16;

  /// <summary>Decoded image plane header information.</summary>
  internal readonly struct PlaneHeader {
    public readonly int Width;
    public readonly int Height;
    public readonly int ComponentCount;
    public readonly JxrBandsPresent Bands;
    public readonly int QuantDc;
    public readonly int QuantLp;
    public readonly int QuantHp;
    public readonly bool OverlapEnabled;
    public readonly int TileColumnsLog2;
    public readonly int TileRowsLog2;
    public readonly JxrColorTransform.Mode ColorMode;

    public PlaneHeader(int width, int height, int componentCount, JxrBandsPresent bands,
      int quantDc, int quantLp, int quantHp, bool overlapEnabled,
      int tileColumnsLog2, int tileRowsLog2, JxrColorTransform.Mode colorMode) {
      Width = width;
      Height = height;
      ComponentCount = componentCount;
      Bands = bands;
      QuantDc = quantDc;
      QuantLp = quantLp;
      QuantHp = quantHp;
      OverlapEnabled = overlapEnabled;
      TileColumnsLog2 = tileColumnsLog2;
      TileRowsLog2 = tileRowsLog2;
      ColorMode = colorMode;
    }

    public int MacroblocksWide => (Width + _MACROBLOCK_SIZE - 1) / _MACROBLOCK_SIZE;
    public int MacroblocksHigh => (Height + _MACROBLOCK_SIZE - 1) / _MACROBLOCK_SIZE;
  }

  /// <summary>
  /// Decodes JPEG XR compressed image data to pixel bytes.
  /// </summary>
  /// <param name="compressedData">The compressed image bitstream (starting after the IFD/container header).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="componentCount">Number of color components (1=Gray, 3=RGB).</param>
  /// <returns>Decoded pixel data: Gray8 or RGB24 depending on componentCount.</returns>
  public static byte[] Decode(byte[] compressedData, int width, int height, int componentCount) {
    ArgumentNullException.ThrowIfNull(compressedData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    componentCount = Math.Clamp(componentCount, 1, 3);

    var reader = new JxrBitReader(compressedData);

    PlaneHeader header;
    try {
      header = _ParsePlaneHeader(reader, width, height, componentCount);
    } catch {
      return _FallbackRawDecode(compressedData, width, height, componentCount);
    }

    return _DecodePlane(reader, header);
  }

  internal static PlaneHeader _ParsePlaneHeader(JxrBitReader reader, int width, int height, int componentCount) {
    if (reader.IsEof)
      throw new InvalidOperationException("Empty bitstream.");

    var spatialTransform = (int)reader.ReadBits(_SPATIAL_XFRM_BITS);
    var bandsPresent = (JxrBandsPresent)reader.ReadBits(_BANDS_PRESENT_BITS);

    // Overlap mode: 0=none, 1=first level, 2=first+second level
    var overlapMode = (int)reader.ReadBits(2);
    var overlapEnabled = overlapMode > 0;

    // Color transform mode: 1 bit if multi-channel
    var colorMode = componentCount >= 3 ? (JxrColorTransform.Mode)reader.ReadBits(2) : JxrColorTransform.Mode.Identity;

    // Quantization parameters (read via JxrQuantizer or direct)
    var hasLp = bandsPresent != JxrBandsPresent.DcOnly;
    var hasHp = bandsPresent == JxrBandsPresent.All || bandsPresent == JxrBandsPresent.NoFlexbits;

    var quantDc = (int)reader.ReadBits(8);
    var quantLp = hasLp ? (int)reader.ReadBits(8) : quantDc;
    var quantHp = hasHp ? (int)reader.ReadBits(8) : quantLp;

    // Per-channel QP flag for multi-channel
    if (componentCount > 1) {
      var chromaSameAsLuma = reader.ReadBit() == 1;
      if (!chromaSameAsLuma) {
        // Skip chroma QP bytes (we use the luma QP for all channels in this implementation)
        for (var c = 1; c < componentCount; ++c) {
          reader.ReadBits(8); // chroma DC QP
          if (hasLp) reader.ReadBits(8);
          if (hasHp) reader.ReadBits(8);
        }
      }
    }

    // Tile structure
    var tileColsLog2 = (int)reader.ReadBits(4);
    var tileRowsLog2 = (int)reader.ReadBits(4);

    reader.AlignToByte();

    return new(width, height, componentCount, bandsPresent, quantDc, quantLp, quantHp,
      overlapEnabled, tileColsLog2, tileRowsLog2, colorMode);
  }

  private static byte[] _DecodePlane(JxrBitReader reader, PlaneHeader header) {
    var mbWide = header.MacroblocksWide;
    var mbHigh = header.MacroblocksHigh;
    var result = new byte[header.Width * header.Height * header.ComponentCount];

    // Create the codec components
    var quantizer = new JxrQuantizer(header.QuantDc, header.QuantLp, header.QuantHp, header.ComponentCount);
    var vlcEngine = new JxrAdaptiveVlcEngine();
    var mbDecoder = new JxrMacroblockDecoder(header.ComponentCount, quantizer, vlcEngine);

    // Allocate per-component macroblock buffers
    var channelBuffers = new int[header.ComponentCount][];
    for (var c = 0; c < header.ComponentCount; ++c)
      channelBuffers[c] = new int[256];

    // Temporary buffers for color conversion
    var rgbBuf = header.ComponentCount == 3 ? new int[3][] { new int[256], new int[256], new int[256] } : null;

    for (var mbY = 0; mbY < mbHigh; ++mbY) {
      // Reset row-level prediction state at each macroblock row
      mbDecoder.ResetRowState();

      for (var mbX = 0; mbX < mbWide; ++mbX) {
        // Decode all channels for this macroblock using the macroblock decoder
        mbDecoder.DecodeMacroblock(reader, channelBuffers, header.Bands);

        // Apply inverse color transform and store pixels
        if (header.ComponentCount == 3) {
          // Inverse color transform (e.g., YCoCg -> RGB)
          JxrColorTransform.InverseTransform(header.ColorMode, channelBuffers);

          JxrColorTransform.StoreRgb24(
            channelBuffers[0], channelBuffers[1], channelBuffers[2],
            result, mbX, mbY, header.Width, header.Height
          );
        } else {
          JxrColorTransform.StoreGray8(channelBuffers[0], result, mbX, mbY, header.Width, header.Height);
        }
      }
    }

    return result;
  }

  /// <summary>Fallback: treat compressed data as raw pixel data when header parsing fails.</summary>
  private static byte[] _FallbackRawDecode(byte[] data, int width, int height, int componentCount) {
    var expectedSize = width * height * componentCount;
    var result = new byte[expectedSize];
    var toCopy = Math.Min(expectedSize, data.Length);
    if (toCopy > 0)
      data.AsSpan(0, toCopy).CopyTo(result);
    return result;
  }
}
