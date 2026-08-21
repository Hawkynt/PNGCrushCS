using System;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// DXT1/BC1, DXT5/BC3 and RGTC1/BC4 block decoding, read from the OpenGL S3TC and RGTC extension
/// specifications the Hap format names as external references rather than restating.
/// </summary>
/// <remarks>
/// A block decode of its own rather than a reuse of <c>FileFormat.Core.BlockDecoders</c>, which this
/// package's DDS and KTX readers use, because two things about it measure differently against ffmpeg's
/// Hap decode than what that shared code does.
/// <para/>
/// <b>The interpolated third and fourth colours of a four-colour block, and the six interpolated steps
/// of an alpha ramp, are a plain integer division with no rounding term</b> — <c>(2*color0+color1)/3</c>,
/// <c>(6*alpha0+alpha1)/7</c> — which is what the S3TC and RGTC extension texts themselves give,
/// literally, and not what the shared decoders do: those round both to the nearest whole value instead.
/// Measured against the corpus below, the rounded reading disagreed at a maximum delta of 2 and the
/// literal one came out bit-exact — confirmed, not merely assumed, against the oracle that settles
/// every other measurement in this decoder.
/// <para/>
/// <b>The 5-bit and 6-bit colour-endpoint expansion is not written down anywhere Hap or S3TC name.</b>
/// The S3TC text says only that a colour is "unpacked ... as though a 16-bit packed pixel with a type
/// of UNSIGNED_SHORT_5_6_5", which states no arithmetic. Bit replication — the exact linear scaling,
/// and the convention <c>FileFormat.Core.BlockDecoders</c> is built on — gets four of the thirty-two
/// five-bit values wrong; a single rounding or truncating division by 31, at every constant added
/// before the divide from 0 to 30, still gets at least one wrong. <see cref="_Expand5"/> and
/// <see cref="_Expand6"/> are read directly off ffmpeg's own decode instead: every one of the
/// thirty-two five-bit and sixty-four six-bit values, at a colour index the extension text defines as
/// an endpoint outright — <c>code(x,y)</c> 0 or 1, <c>RGB0</c> or <c>RGB1</c>, no interpolation
/// involved — appearing hundreds to hundreds of thousands of times across the corpus below and never
/// once disagreeing with another occurrence of the same input. Red and blue, both five-bit fields,
/// read the identical table.
/// </remarks>
internal static class HapBlockDecoding {

  /// <summary>Decodes a DXT1/BC1 image to RGB, three bytes a pixel, dropping the format's optional
  /// one-bit alpha — Hap's "RGB" pixel format has no channel for it to occupy.</summary>
  public static byte[] DecodeDxt1ToRgb(ReadOnlySpan<byte> data, int width, int height) {
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var stride = width * 3;
    var output = new byte[height * stride];

    Span<byte> palR = stackalloc byte[4];
    Span<byte> palG = stackalloc byte[4];
    Span<byte> palB = stackalloc byte[4];

    for (var by = 0; by < blocksY; ++by)
    for (var bx = 0; bx < blocksX; ++bx) {
      var block = data.Slice((by * blocksX + bx) * 8, 8);
      _DecodeColourBlock(block, palR, palG, palB);

      for (var py = 0; py < 4; ++py) {
        var imgY = by * 4 + py;
        if (imgY >= height)
          break;

        for (var px = 0; px < 4; ++px) {
          var imgX = bx * 4 + px;
          if (imgX >= width)
            break;

          var i = py * 4 + px;
          var idx = (block[4 + i / 4] >> ((i % 4) * 2)) & 0x3;
          var dst = imgY * stride + imgX * 3;
          output[dst] = palR[idx];
          output[dst + 1] = palG[idx];
          output[dst + 2] = palB[idx];
        }
      }
    }

    return output;
  }

  /// <summary>Decodes a DXT5/BC3 image to raw RGBA, four bytes a pixel — the colour part's red, green
  /// and blue samples and the alpha part's interpolated value, with no meaning imposed on any of
  /// them. RGBA DXT5 uses all four as colour; Scaled YCoCg DXT5 reads the same four bytes as chroma,
  /// scale and luma.</summary>
  public static byte[] DecodeDxt5Raw(ReadOnlySpan<byte> data, int width, int height) {
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var stride = width * 4;
    var output = new byte[height * stride];

    Span<byte> palR = stackalloc byte[4];
    Span<byte> palG = stackalloc byte[4];
    Span<byte> palB = stackalloc byte[4];
    Span<byte> alphas = stackalloc byte[8];

    for (var by = 0; by < blocksY; ++by)
    for (var bx = 0; bx < blocksX; ++bx) {
      var block = data.Slice((by * blocksX + bx) * 16, 16);
      var colourBlock = block[8..16];
      _DecodeColourBlock(colourBlock, palR, palG, palB);
      _InterpolateAlpha(block[0], block[1], alphas);
      var alphaIndices = _UnpackAlphaIndices(block[2..8]);

      for (var py = 0; py < 4; ++py) {
        var imgY = by * 4 + py;
        if (imgY >= height)
          break;

        for (var px = 0; px < 4; ++px) {
          var imgX = bx * 4 + px;
          if (imgX >= width)
            break;

          var i = py * 4 + px;
          var idx = (colourBlock[4 + i / 4] >> ((i % 4) * 2)) & 0x3;
          var dst = imgY * stride + imgX * 4;
          output[dst] = palR[idx];
          output[dst + 1] = palG[idx];
          output[dst + 2] = palB[idx];
          output[dst + 3] = alphas[alphaIndices[i]];
        }
      }
    }

    return output;
  }

  /// <summary>Decodes an RGTC1/BC4 image to a single 8-bit sample a pixel — the format is DXT5's
  /// alpha block on its own, used to carry a single channel with the same eight-sample precision.</summary>
  public static byte[] DecodeRgtc1(ReadOnlySpan<byte> data, int width, int height) {
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var output = new byte[height * width];

    Span<byte> values = stackalloc byte[8];

    for (var by = 0; by < blocksY; ++by)
    for (var bx = 0; bx < blocksX; ++bx) {
      var block = data.Slice((by * blocksX + bx) * 8, 8);
      _InterpolateAlpha(block[0], block[1], values);
      var indices = _UnpackAlphaIndices(block[2..8]);

      for (var py = 0; py < 4; ++py) {
        var imgY = by * 4 + py;
        if (imgY >= height)
          break;

        for (var px = 0; px < 4; ++px) {
          var imgX = bx * 4 + px;
          if (imgX >= width)
            break;

          output[imgY * width + imgX] = values[indices[py * 4 + px]];
        }
      }
    }

    return output;
  }

  private static void _DecodeColourBlock(ReadOnlySpan<byte> block, Span<byte> palR, Span<byte> palG, Span<byte> palB) {
    var c0Raw = (ushort)(block[0] | (block[1] << 8));
    var c1Raw = (ushort)(block[2] | (block[3] << 8));
    _DecodeRgb565(c0Raw, out var r0, out var g0, out var b0);
    _DecodeRgb565(c1Raw, out var r1, out var g1, out var b1);

    palR[0] = r0; palG[0] = g0; palB[0] = b0;
    palR[1] = r1; palG[1] = g1; palB[1] = b1;

    if (c0Raw > c1Raw) {
      palR[2] = (byte)((2 * r0 + r1) / 3);
      palG[2] = (byte)((2 * g0 + g1) / 3);
      palB[2] = (byte)((2 * b0 + b1) / 3);
      palR[3] = (byte)((r0 + 2 * r1) / 3);
      palG[3] = (byte)((g0 + 2 * g1) / 3);
      palB[3] = (byte)((b0 + 2 * b1) / 3);
    } else {
      palR[2] = (byte)((r0 + r1) / 2);
      palG[2] = (byte)((g0 + g1) / 2);
      palB[2] = (byte)((b0 + b1) / 2);
      palR[3] = 0; palG[3] = 0; palB[3] = 0;
    }
  }

  private static void _InterpolateAlpha(byte a0, byte a1, Span<byte> values) {
    values[0] = a0;
    values[1] = a1;

    if (a0 > a1) {
      values[2] = (byte)((6 * a0 + 1 * a1) / 7);
      values[3] = (byte)((5 * a0 + 2 * a1) / 7);
      values[4] = (byte)((4 * a0 + 3 * a1) / 7);
      values[5] = (byte)((3 * a0 + 4 * a1) / 7);
      values[6] = (byte)((2 * a0 + 5 * a1) / 7);
      values[7] = (byte)((1 * a0 + 6 * a1) / 7);
    } else {
      values[2] = (byte)((4 * a0 + 1 * a1) / 5);
      values[3] = (byte)((3 * a0 + 2 * a1) / 5);
      values[4] = (byte)((2 * a0 + 3 * a1) / 5);
      values[5] = (byte)((1 * a0 + 4 * a1) / 5);
      values[6] = 0;
      values[7] = 255;
    }
  }

  private static byte[] _UnpackAlphaIndices(ReadOnlySpan<byte> packed) {
    var indices = new byte[16];
    var bits0 = (uint)packed[0] | ((uint)packed[1] << 8) | ((uint)packed[2] << 16);
    for (var i = 0; i < 8; ++i)
      indices[i] = (byte)((bits0 >> (i * 3)) & 0x07);

    var bits1 = (uint)packed[3] | ((uint)packed[4] << 8) | ((uint)packed[5] << 16);
    for (var i = 0; i < 8; ++i)
      indices[8 + i] = (byte)((bits1 >> (i * 3)) & 0x07);

    return indices;
  }

  /// <summary>
  /// The 5-bit-to-8-bit colour-endpoint expansion, measured rather than read from a formula — see this
  /// type's own remarks above.
  /// </summary>
  private static readonly byte[] _Expand5 = [
    0, 8, 16, 25, 33, 41, 49, 58, 66, 74, 82, 90, 99, 107, 115, 123,
    132, 140, 148, 156, 164, 173, 181, 189, 197, 205, 214, 222, 230, 238, 247, 255,
  ];

  /// <summary>The 6-bit-to-8-bit expansion, read off the same measurement as <see cref="_Expand5"/>.</summary>
  private static readonly byte[] _Expand6 = [
    0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 45, 49, 53, 57, 61,
    65, 69, 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 113, 117, 121, 125,
    130, 134, 138, 142, 146, 150, 154, 158, 162, 166, 170, 174, 178, 182, 186, 190,
    194, 198, 202, 206, 210, 214, 219, 223, 227, 231, 235, 239, 243, 247, 251, 255,
  ];

  private static void _DecodeRgb565(ushort value, out byte r, out byte g, out byte b) {
    var r5 = (value >> 11) & 0x1F;
    var g6 = (value >> 5) & 0x3F;
    var b5 = value & 0x1F;
    r = _Expand5[r5];
    g = _Expand6[g6];
    b = _Expand5[b5];
  }
}
