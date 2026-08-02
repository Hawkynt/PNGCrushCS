using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes ETC2 and EAC compressed texture blocks (RGB, RGBA, R11, RG11, and punchthrough alpha variants).</summary>
public static class Etc2Decoder {

  /// <summary>EAC modifier table (8 entries of 8 modifiers each) per the Khronos ETC2/EAC spec.</summary>
  private static readonly int[][] _EacModifierTable = [
    [-3, -6, -9, -15, 2, 5, 8, 14],
    [-3, -7, -10, -13, 2, 6, 9, 12],
    [-2, -5, -8, -13, 1, 4, 7, 12],
    [-2, -4, -6, -13, 1, 3, 5, 12],
    [-3, -6, -8, -12, 2, 5, 7, 11],
    [-3, -7, -9, -11, 2, 6, 8, 10],
    [-4, -7, -8, -11, 3, 6, 7, 10],
    [-3, -5, -8, -11, 2, 4, 7, 10],
  ];

  /// <summary>
  /// Whether a block is one of the three arrangements ETC2 adds to ETC1, which are not decoded here.
  /// </summary>
  /// <remarks>
  /// ETC2 reuses the bit patterns that ETC1 could not produce. In differential mode each channel
  /// holds a five-bit base and a three-bit signed delta, and ETC1 never lets the sum leave the
  /// five-bit range; ETC2 gives each overflow a meaning of its own — red for the T arrangement,
  /// green for the H one, blue for the planar one.
  /// <para/>
  /// So the test is arithmetic rather than a guess: whichever channel overflows says which
  /// arrangement the block is in, and none of the three resembles what ETC1 would make of the same
  /// bits. Decoding them as ETC1 anyway, which is what used to happen, gives a block of unrelated
  /// colours and reports no trouble.
  /// </remarks>
  private static bool _IsEtc2Only(ReadOnlySpan<byte> block) {
    // Bit 1 of the fourth byte selects differential mode; the individual mode is plain ETC1.
    if ((block[3] & 2) == 0)
      return false;

    for (var channel = 0; channel < 3; ++channel) {
      var value = block[channel];
      var basis = value >> 3;
      var delta = value & 7;
      if (delta > 3)
        delta -= 8;

      var sum = basis + delta;
      if (sum is < 0 or > 31)
        return true;
    }

    return false;
  }

  /// <summary>Decodes a single ETC2 RGB block (8 bytes) into 64 bytes of RGBA pixel data.</summary>
  /// <returns>Whether the block was one this decodes; the T, H and planar arrangements are not.</returns>
  public static bool DecodeEtc2RgbBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    if (_IsEtc2Only(block)) {
      output[..64].Clear();
      return false;
    }

    Etc1Decoder.DecodeBlock(block, output);
    return true;
  }

  /// <summary>Decodes a single ETC2 RGBA block (16 bytes: 8-byte EAC alpha + 8-byte ETC2 RGB) into 64 bytes of RGBA pixel data.</summary>
  public static bool DecodeEtc2RgbaBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    // Decode RGB from the second 8 bytes
    var decoded = DecodeEtc2RgbBlock(block.Slice(8, 8), output);

    // Decode EAC alpha from the first 8 bytes and overwrite alpha channel
    _DecodeEacAlpha(block.Slice(0, 8), output);
    return decoded;
  }

  /// <summary>Decodes a single EAC R11 block (8 bytes) into 64 bytes of RGBA pixel data (R channel only, G=0, B=0, A=255).</summary>
  public static bool DecodeEacR11Block(ReadOnlySpan<byte> block, Span<byte> output) {
    // Clear output to zero, then set alpha to 255
    output.Slice(0, 64).Clear();
    for (var i = 0; i < 16; ++i)
      output[i * 4 + 3] = 255;

    // Decode EAC channel into the R component
    _DecodeEacChannel(block, output, 0);
    return true;
  }

  /// <summary>Decodes a single EAC RG11 block (16 bytes: two EAC blocks) into 64 bytes of RGBA pixel data (R from first, G from second, B=0, A=255).</summary>
  public static bool DecodeEacRg11Block(ReadOnlySpan<byte> block, Span<byte> output) {
    // Clear output to zero, then set alpha to 255
    output.Slice(0, 64).Clear();
    for (var i = 0; i < 16; ++i)
      output[i * 4 + 3] = 255;

    // Decode first EAC block into R channel
    _DecodeEacChannel(block.Slice(0, 8), output, 0);

    // Decode second EAC block into G channel
    _DecodeEacChannel(block.Slice(8, 8), output, 1);
    return true;
  }

  /// <summary>Decodes a single ETC2 RGB with punchthrough alpha block (8 bytes) into 64 bytes of RGBA pixel data.</summary>
  /// <remarks>
  /// The punchthrough arrangement is not decoded: where it marks a pixel fully transparent this
  /// would draw the colour instead, so a block using it is reported rather than guessed at.
  /// </remarks>
  public static bool DecodeEtc2RgbA1Block(ReadOnlySpan<byte> block, Span<byte> output) {
    // Bit 1 of the fourth byte carries the opacity flag here rather than selecting differential
    // mode, and a clear flag is exactly the case this does not handle.
    if ((block[3] & 2) == 0) {
      output[..64].Clear();
      return false;
    }

    return DecodeEtc2RgbBlock(block, output);
  }

  /// <summary>Decodes a full ETC2 RGB image (8 bytes/block) into RGBA pixel data.</summary>
  public static int DecodeEtc2RgbImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEtc2RgbBlock);

  /// <summary>Decodes a full ETC2 RGBA image (16 bytes/block) into RGBA pixel data.</summary>
  public static int DecodeEtc2RgbaImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 16, output, DecodeEtc2RgbaBlock);

  /// <summary>Decodes a full EAC R11 image (8 bytes/block) into RGBA pixel data.</summary>
  public static int DecodeEacR11Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEacR11Block);

  /// <summary>Decodes a full EAC RG11 image (16 bytes/block) into RGBA pixel data.</summary>
  public static int DecodeEacRg11Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 16, output, DecodeEacRg11Block);

  /// <summary>Decodes a full ETC2 punchthrough alpha image (8 bytes/block) into RGBA pixel data.</summary>
  public static int DecodeEtc2RgbA1Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEtc2RgbA1Block);

  /// <summary>Decodes EAC alpha data (8 bytes) and writes the alpha channel into existing RGBA output.</summary>
  private static void _DecodeEacAlpha(ReadOnlySpan<byte> block, Span<byte> output) {
    var baseAlpha = block[0];
    var multiplier = (block[1] >> 4) & 0xF;
    var tableIndex = block[1] & 0xF;
    var modifiers = _EacModifierTable[tableIndex];

    // Extract 3-bit indices for 16 pixels from bytes 2-7 (48 bits total, MSB first)
    // Bit layout is column-major: pixel (x,y) -> bit index x*4+y
    for (var x = 0; x < 4; ++x) {
      for (var y = 0; y < 4; ++y) {
        var pixelBitIndex = x * 4 + y;
        var bitOffset = pixelBitIndex * 3;
        var pixelIdx = _Extract3Bits(block.Slice(2, 6), bitOffset);
        var modifier = modifiers[pixelIdx];

        int alpha;
        if (multiplier != 0)
          alpha = Math.Clamp(baseAlpha + multiplier * modifier, 0, 255);
        else
          alpha = Math.Clamp(baseAlpha + modifier, 0, 255);

        var outOffset = (y * 4 + x) * 4 + 3;
        output[outOffset] = (byte)alpha;
      }
    }
  }

  /// <summary>Decodes an EAC channel (8 bytes) and writes values into a specific channel offset of existing RGBA output.</summary>
  private static void _DecodeEacChannel(ReadOnlySpan<byte> block, Span<byte> output, int channelOffset) {
    var baseValue = block[0];
    var multiplier = (block[1] >> 4) & 0xF;
    var tableIndex = block[1] & 0xF;
    var modifiers = _EacModifierTable[tableIndex];

    for (var x = 0; x < 4; ++x) {
      for (var y = 0; y < 4; ++y) {
        var pixelBitIndex = x * 4 + y;
        var bitOffset = pixelBitIndex * 3;
        var pixelIdx = _Extract3Bits(block.Slice(2, 6), bitOffset);
        var modifier = modifiers[pixelIdx];

        int value;
        if (multiplier != 0)
          value = Math.Clamp(baseValue + multiplier * modifier, 0, 255);
        else
          value = Math.Clamp(baseValue + modifier, 0, 255);

        var outOffset = (y * 4 + x) * 4 + channelOffset;
        output[outOffset] = (byte)value;
      }
    }
  }

  /// <summary>Extracts a 3-bit value from a 6-byte span at the given bit offset (MSB first, big-endian bit order).</summary>
  private static int _Extract3Bits(ReadOnlySpan<byte> data, int bitOffset) {
    // Total 48 bits packed into 6 bytes, MSB first
    var bytePos = bitOffset >> 3;
    var bitPos = bitOffset & 7;

    // Read 16 bits starting from the byte containing our 3-bit value
    int raw;
    if (bytePos + 1 < data.Length)
      raw = (data[bytePos] << 8) | data[bytePos + 1];
    else
      raw = data[bytePos] << 8;

    // Extract 3 bits at the correct position
    var shift = 16 - bitPos - 3;
    return (raw >> shift) & 7;
  }

  /// <summary>Generic block-based image decoder that iterates 4x4 blocks and copies to the output buffer.</summary>
  /// <returns>How many blocks used an arrangement this does not decode.</returns>
  private static int _DecodeImage(ReadOnlySpan<byte> data, int width, int height, int blockBytes, Span<byte> output, _BlockDecoder decoder) {
    Span<byte> blockPixels = stackalloc byte[64];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var blockIndex = 0;
    var undecoded = 0;

    for (var by = 0; by < blocksY; ++by) {
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = blockIndex * blockBytes;
        if (blockOffset + blockBytes > data.Length)
          return undecoded + (blocksY - by) * blocksX - bx;

        if (!decoder(data.Slice(blockOffset, blockBytes), blockPixels))
          ++undecoded;

        var px = bx * 4;
        var py = by * 4;
        for (var y = 0; y < 4 && py + y < height; ++y)
          for (var x = 0; x < 4 && px + x < width; ++x) {
            var srcOffset = (y * 4 + x) * 4;
            var dstOffset = ((py + y) * width + (px + x)) * 4;
            output[dstOffset] = blockPixels[srcOffset];
            output[dstOffset + 1] = blockPixels[srcOffset + 1];
            output[dstOffset + 2] = blockPixels[srcOffset + 2];
            output[dstOffset + 3] = blockPixels[srcOffset + 3];
          }

        ++blockIndex;
      }
    }

    return undecoded;
  }

  private delegate bool _BlockDecoder(ReadOnlySpan<byte> block, Span<byte> output);
}
