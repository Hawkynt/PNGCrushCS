using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC7 compressed blocks (16 bytes -> 4x4 RGBA32 pixels). Supports all 8 modes with 1-3 subsets.</summary>
public static class Bc7Decoder {

  private const int _BLOCK_SIZE = 16;
  private const int _BLOCK_DIM = 4;
  private const int _PIXELS_PER_BLOCK = _BLOCK_DIM * _BLOCK_DIM;
  private const int _BYTES_PER_PIXEL = 4;

  // Mode table: [subsets, partitionBits, rotationBits, indexSelBits, colorBits, alphaBits, endpointPBits, sharedPBits, indexBits0, indexBits1]
  private static readonly int[,] _ModeTable = {
    // mode 0: 3 subsets, 4 part bits, 0 rot, 0 idxSel, 4 color, 0 alpha, 1 pbit/ep, 0 shared, 3 idx, 0
    { 3, 4, 0, 0, 4, 0, 1, 0, 3, 0 },
    // mode 1: 2 subsets, 6 part bits, 0 rot, 0 idxSel, 6 color, 0 alpha, 0 pbit/ep, 1 shared, 3 idx, 0
    { 2, 6, 0, 0, 6, 0, 0, 1, 3, 0 },
    // mode 2: 3 subsets, 6 part bits, 0 rot, 0 idxSel, 5 color, 0 alpha, 0 pbit/ep, 0 shared, 2 idx, 0
    { 3, 6, 0, 0, 5, 0, 0, 0, 2, 0 },
    // mode 3: 2 subsets, 6 part bits, 0 rot, 0 idxSel, 7 color, 0 alpha, 1 pbit/ep, 0 shared, 2 idx, 0
    { 2, 6, 0, 0, 7, 0, 1, 0, 2, 0 },
    // mode 4: 1 subset, 0 part bits, 2 rot, 1 idxSel, 5 color, 6 alpha, 0 pbit/ep, 0 shared, 2 idx, 3 idx2
    { 1, 0, 2, 1, 5, 6, 0, 0, 2, 3 },
    // mode 5: 1 subset, 0 part bits, 2 rot, 0 idxSel, 7 color, 8 alpha, 0 pbit/ep, 0 shared, 2 idx, 2 idx2
    { 1, 0, 2, 0, 7, 8, 0, 0, 2, 2 },
    // mode 6: 1 subset, 0 part bits, 0 rot, 0 idxSel, 7 color, 7 alpha, 1 pbit/ep, 0 shared, 4 idx, 0
    { 1, 0, 0, 0, 7, 7, 1, 0, 4, 0 },
    // mode 7: 2 subsets, 6 part bits, 0 rot, 0 idxSel, 5 color, 5 alpha, 1 pbit/ep, 0 shared, 2 idx, 0
    { 2, 6, 0, 0, 5, 5, 1, 0, 2, 0 },
  };

  // 64 partition patterns for 2-subset mode (each 16 entries, values 0 or 1)
  private static readonly byte[,] _Partitions2 = _BuildPartitions2();
  // 64 partition patterns for 3-subset mode (each 16 entries, values 0, 1, or 2)
  private static readonly byte[,] _Partitions3 = _BuildPartitions3();

  // Anchor index for second subset in 2-subset partitioning
  private static readonly byte[] _AnchorIndex2_1 = [
    15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
    15,  2,  8,  2,  2,  8,  8, 15,  2,  8,  2,  2,  8,  8,  2,  2,
    15, 15,  6,  8,  2,  8, 15, 15,  2,  8,  2,  2,  2, 15, 15,  6,
     6,  2,  6,  8, 15, 15,  2,  2, 15, 15, 15, 15, 15,  2,  2, 15
  ];

  // Anchor index for second subset in 3-subset partitioning
  private static readonly byte[] _AnchorIndex3_1 = [
     3,  3, 15, 15,  8,  3, 15, 15,  8,  8,  6,  6,  6,  5,  3,  3,
     3,  3,  8, 15,  3,  3,  6, 10,  5,  8,  8,  6,  8,  5, 15, 15,
     8, 15,  3,  5,  6, 10,  8, 15, 15,  3, 15,  5, 15, 15, 15, 15,
     3, 15,  5,  5,  5,  8,  5, 10,  5, 10,  8, 13, 15, 12,  3,  3
  ];

  // Anchor index for third subset in 3-subset partitioning
  private static readonly byte[] _AnchorIndex3_2 = [
    15,  8,  8,  3, 15, 15,  3,  8, 15, 15, 15, 15, 15, 15, 15,  8,
    15,  8, 15,  3, 15,  8, 15,  8,  3, 15,  6, 10, 15, 15, 10,  8,
    15,  3, 15, 10, 10,  8,  9, 10,  6, 15,  8, 15,  3,  6,  6,  8,
    15,  3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,  3, 15, 15,  8
  ];

  /// <summary>Decodes a single 4x4 BC7 block (16 bytes) to 64 bytes of RGBA32 pixel data.</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    // Determine mode from leading bit
    int mode;
    for (mode = 0; mode < 8; ++mode)
      if ((block[0] & (1 << mode)) != 0)
        break;

    if (mode >= 8) {
      // Reserved mode: output transparent black
      output.Slice(0, _PIXELS_PER_BLOCK * _BYTES_PER_PIXEL).Clear();
      return;
    }

    var bitPos = mode + 1;

    var subsets = _ModeTable[mode, 0];
    var partitionBits = _ModeTable[mode, 1];
    var rotationBits = _ModeTable[mode, 2];
    var indexSelBits = _ModeTable[mode, 3];
    var colorBits = _ModeTable[mode, 4];
    var alphaBits = _ModeTable[mode, 5];
    var epPBits = _ModeTable[mode, 6];
    var sharedPBits = _ModeTable[mode, 7];
    var indexBits0 = _ModeTable[mode, 8];
    var indexBits1 = _ModeTable[mode, 9];

    var partition = _ReadBits(block, ref bitPos, partitionBits);
    var rotation = _ReadBits(block, ref bitPos, rotationBits);
    var indexSel = _ReadBits(block, ref bitPos, indexSelBits);

    // Read endpoints
    var numEndpoints = subsets * 2;
    Span<byte> endpointR = stackalloc byte[6];
    Span<byte> endpointG = stackalloc byte[6];
    Span<byte> endpointB = stackalloc byte[6];
    Span<byte> endpointA = stackalloc byte[6];

    for (var i = 0; i < numEndpoints; ++i)
      endpointR[i] = (byte)_ReadBits(block, ref bitPos, colorBits);
    for (var i = 0; i < numEndpoints; ++i)
      endpointG[i] = (byte)_ReadBits(block, ref bitPos, colorBits);
    for (var i = 0; i < numEndpoints; ++i)
      endpointB[i] = (byte)_ReadBits(block, ref bitPos, colorBits);
    for (var i = 0; i < numEndpoints; ++i)
      endpointA[i] = alphaBits > 0 ? (byte)_ReadBits(block, ref bitPos, alphaBits) : (byte)255;

    // Read P-bits
    Span<int> pBits = stackalloc int[6];
    if (epPBits > 0)
      for (var i = 0; i < numEndpoints; ++i)
        pBits[i] = _ReadBits(block, ref bitPos, 1);
    else if (sharedPBits > 0)
      for (var i = 0; i < subsets; ++i) {
        var pb = _ReadBits(block, ref bitPos, 1);
        pBits[i * 2] = pb;
        pBits[i * 2 + 1] = pb;
      }

    // Unquantize endpoints
    for (var i = 0; i < numEndpoints; ++i) {
      endpointR[i] = _Unquantize(endpointR[i], colorBits, pBits[i], epPBits + sharedPBits > 0);
      endpointG[i] = _Unquantize(endpointG[i], colorBits, pBits[i], epPBits + sharedPBits > 0);
      endpointB[i] = _Unquantize(endpointB[i], colorBits, pBits[i], epPBits + sharedPBits > 0);
      if (alphaBits > 0)
        endpointA[i] = _Unquantize(endpointA[i], alphaBits, pBits[i], epPBits + sharedPBits > 0);
    }

    // Read index data
    Span<byte> indices0 = stackalloc byte[_PIXELS_PER_BLOCK];
    Span<byte> indices1 = stackalloc byte[_PIXELS_PER_BLOCK];
    var hasSecondIndex = indexBits1 > 0;

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var bits = indexBits0;
      if (_IsAnchorIndex(i, subsets, partition))
        --bits;
      indices0[i] = (byte)_ReadBits(block, ref bitPos, bits);
    }

    if (hasSecondIndex)
      for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
        var bits = indexBits1;
        if (_IsAnchorIndex(i, 1, 0)) // second index only has anchor at 0
          --bits;
        indices1[i] = (byte)_ReadBits(block, ref bitPos, bits);
      }

    // Interpolate and write pixels
    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var subset = subsets == 1 ? 0 : subsets == 2 ? _Partitions2[partition, i] : _Partitions3[partition, i];
      var e0 = subset * 2;
      var e1 = subset * 2 + 1;

      byte r, g, b, a;
      if (hasSecondIndex) {
        var colorIdx = indexSel == 0 ? indices0[i] : indices1[i];
        var alphaIdx = indexSel == 0 ? indices1[i] : indices0[i];
        var colorWeight = _InterpolationWeights(indexSel == 0 ? indexBits0 : indexBits1, colorIdx);
        var alphaWeight = _InterpolationWeights(indexSel == 0 ? indexBits1 : indexBits0, alphaIdx);
        r = _Interpolate(endpointR[e0], endpointR[e1], colorWeight);
        g = _Interpolate(endpointG[e0], endpointG[e1], colorWeight);
        b = _Interpolate(endpointB[e0], endpointB[e1], colorWeight);
        a = _Interpolate(endpointA[e0], endpointA[e1], alphaWeight);
      } else {
        var weight = _InterpolationWeights(indexBits0, indices0[i]);
        r = _Interpolate(endpointR[e0], endpointR[e1], weight);
        g = _Interpolate(endpointG[e0], endpointG[e1], weight);
        b = _Interpolate(endpointB[e0], endpointB[e1], weight);
        a = alphaBits > 0 ? _Interpolate(endpointA[e0], endpointA[e1], weight) : (byte)255;
      }

      // Apply rotation
      switch (rotation) {
        case 1: (a, r) = (r, a); break;
        case 2: (a, g) = (g, a); break;
        case 3: (a, b) = (b, a); break;
      }

      var off = i * _BYTES_PER_PIXEL;
      output[off] = r;
      output[off + 1] = g;
      output[off + 2] = b;
      output[off + 3] = a;
    }
  }

  /// <summary>Decodes a full BC7-compressed image to RGBA32 scanline-order pixel data.</summary>
  public static void DecodeImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output) {
    var blocksX = (width + _BLOCK_DIM - 1) / _BLOCK_DIM;
    var blocksY = (height + _BLOCK_DIM - 1) / _BLOCK_DIM;
    var stride = width * _BYTES_PER_PIXEL;
    Span<byte> blockPixels = stackalloc byte[_PIXELS_PER_BLOCK * _BYTES_PER_PIXEL];

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = (by * blocksX + bx) * _BLOCK_SIZE;
        if (blockOffset + _BLOCK_SIZE > data.Length)
          return;

        DecodeBlock(data.Slice(blockOffset, _BLOCK_SIZE), blockPixels);

        for (var py = 0; py < _BLOCK_DIM; ++py) {
          var imgY = by * _BLOCK_DIM + py;
          if (imgY >= height)
            break;

          for (var px = 0; px < _BLOCK_DIM; ++px) {
            var imgX = bx * _BLOCK_DIM + px;
            if (imgX >= width)
              break;

            var srcOffset = (py * _BLOCK_DIM + px) * _BYTES_PER_PIXEL;
            var dstOffset = imgY * stride + imgX * _BYTES_PER_PIXEL;
            output[dstOffset] = blockPixels[srcOffset];
            output[dstOffset + 1] = blockPixels[srcOffset + 1];
            output[dstOffset + 2] = blockPixels[srcOffset + 2];
            output[dstOffset + 3] = blockPixels[srcOffset + 3];
          }
        }
      }
  }

  private static int _ReadBits(ReadOnlySpan<byte> data, ref int bitPos, int numBits) {
    if (numBits == 0)
      return 0;

    var result = 0;
    for (var i = 0; i < numBits; ++i) {
      var byteIdx = bitPos >> 3;
      var bitIdx = bitPos & 7;
      result |= ((data[byteIdx] >> bitIdx) & 1) << i;
      ++bitPos;
    }
    return result;
  }

  private static byte _Unquantize(byte value, int bits, int pBit, bool hasPBit) {
    if (hasPBit) {
      var total = bits + 1;
      var expanded = (value << 1) | pBit;
      return (byte)((expanded << (8 - total)) | (expanded >> (total + total - 8)));
    }

    if (bits >= 8)
      return value;

    return (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));
  }

  private static byte _Interpolate(byte e0, byte e1, int weight) =>
    (byte)((e0 * (64 - weight) + e1 * weight + 32) >> 6);

  // BC7 interpolation weights for 2, 3, and 4-bit indices
  private static readonly int[] _Weights2 = [0, 21, 43, 64];
  private static readonly int[] _Weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
  private static readonly int[] _Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

  private static int _InterpolationWeights(int indexBits, int index) => indexBits switch {
    2 => _Weights2[index],
    3 => _Weights3[index],
    4 => _Weights4[index],
    _ => 0
  };

  private static bool _IsAnchorIndex(int pixelIndex, int subsets, int partition) {
    if (pixelIndex == 0)
      return true;

    return subsets switch {
      2 => pixelIndex == _AnchorIndex2_1[partition],
      3 => pixelIndex == _AnchorIndex3_1[partition] || pixelIndex == _AnchorIndex3_2[partition],
      _ => false
    };
  }

  // BC7 partition table data (from the specification)
  private static byte[,] _BuildPartitions2() {
    // 64 partition patterns for 2-subset mode
    var table = new byte[64, 16];
    ReadOnlySpan<uint> patterns = [
      0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80,
      0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
      0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE,
      0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
      0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A,
      0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
      0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C,
      0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22
    ];

    for (var p = 0; p < 64; ++p)
      for (var i = 0; i < 16; ++i)
        table[p, i] = (byte)((patterns[p] >> i) & 1);

    return table;
  }

  private static byte[,] _BuildPartitions3() {
    // 64 partition patterns for 3-subset mode (packed as 2 bits per pixel, 32 bits total)
    var table = new byte[64, 16];
    ReadOnlySpan<uint> patterns = [
      0xAA685050, 0x6A5A5040, 0x5A5A4200, 0x5450A0A8, 0x5A5A4200, 0xAA5B5050, 0xAA5B5050, 0xAA5B5050,
      0x2A1B0500, 0x5515A0A0, 0x55154000, 0xAA5B5050, 0x55556A40, 0xA0555400, 0x55554200, 0x5A554400,
      0xAA555000, 0xA0A4A200, 0x5A5A5000, 0x2A050500, 0x5A5A5050, 0x55150000, 0x555A5000, 0xAA5B5050,
      0x6A555040, 0xA0555000, 0x55555200, 0x56555000, 0x55544000, 0x55555000, 0x6A555040, 0xFFFE5050,
      0x5AAF5450, 0x5A5A5050, 0x55A50000, 0x55A50000, 0x5A5A5050, 0x5A5A5050, 0x55FF0000, 0x5AFF5050,
      0x5AFF5050, 0x1BF50500, 0x5AFF5050, 0xAA5F5050, 0x5AFF5050, 0x55FF5050, 0x5FFF5050, 0x5AFF5050,
      0xFF555050, 0xAA555050, 0xABFF5050, 0x5FFF0050, 0xFF550050, 0xFF555050, 0xAA555050, 0x5AFF5050,
      0x5AFF0050, 0x555F5050, 0x5A5A5050, 0x5A5A0050, 0xAAAA5050, 0x5A5A5050, 0xFFFF5050, 0x5A5A5050
    ];

    for (var p = 0; p < 64; ++p)
      for (var i = 0; i < 16; ++i)
        table[p, i] = (byte)((patterns[p] >> (i * 2)) & 3);

    return table;
  }
}
