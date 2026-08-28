using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC6H blocks to their native RGB binary16 samples.</summary>
/// <remarks>
/// Ported to safe C# from Sergii Kudlai's <c>bcdec</c> BC6H decoder, used under its MIT licence.
/// The bit layouts, endpoint transforms, unquantisation, partition fix-up indices and interpolation
/// are the BC6H decoding process rather than a display approximation. The output is three little-
/// endian IEEE 754 binary16 samples per pixel, which is the representation <c>RgbF16</c> expects.
///
/// bcdec copyright (c) 2022 Sergii Kudlai, MIT licensed:
/// Permission is granted, free of charge, to use, copy, modify, merge, publish, distribute,
/// sublicense and/or sell copies, provided the copyright and permission notice are retained.
/// The software is provided "as is", without warranty of any kind.
/// </remarks>
public static class Bc6HFloatDecoder {

  private const int _BLOCK_BYTES = 16;
  private const int _BLOCK_DIM = 4;
  private const int _HALF_BYTES = 2;
  private const int _CHANNELS = 3;
  private const int _BYTES_PER_PIXEL = _HALF_BYTES * _CHANNELS;

  private static readonly byte[,] _ActualBits = {
    { 10, 7, 11, 11, 11, 9, 8, 8, 8, 6, 10, 11, 12, 16 },
    {  5, 6,  5,  4,  4, 5, 6, 5, 5, 6, 10,  9,  8,  4 },
    {  5, 6,  4,  5,  4, 5, 5, 6, 5, 6, 10,  9,  8,  4 },
    {  5, 6,  4,  4,  5, 5, 5, 5, 6, 6, 10,  9,  8,  4 },
  };

  // Low seven bits select a subset; bit seven marks a fix-up index whose MSB is omitted.
  private static readonly byte[,] _Partitions = {
    {128,0,1,1, 0,0,1,1, 0,0,1,1, 0,0,1,129},
    {128,0,0,1, 0,0,0,1, 0,0,0,1, 0,0,0,129},
    {128,1,1,1, 0,1,1,1, 0,1,1,1, 0,1,1,129},
    {128,0,0,1, 0,0,1,1, 0,0,1,1, 0,1,1,129},
    {128,0,0,0, 0,0,0,1, 0,0,0,1, 0,0,1,129},
    {128,0,1,1, 0,1,1,1, 0,1,1,1, 1,1,1,129},
    {128,0,0,1, 0,0,1,1, 0,1,1,1, 1,1,1,129},
    {128,0,0,0, 0,0,0,1, 0,0,1,1, 0,1,1,129},
    {128,0,0,0, 0,0,0,0, 0,0,0,1, 0,0,1,129},
    {128,0,1,1, 0,1,1,1, 1,1,1,1, 1,1,1,129},
    {128,0,0,0, 0,0,0,1, 0,1,1,1, 1,1,1,129},
    {128,0,0,0, 0,0,0,0, 0,0,0,1, 0,1,1,129},
    {128,0,0,1, 0,1,1,1, 1,1,1,1, 1,1,1,129},
    {128,0,0,0, 0,0,0,0, 1,1,1,1, 1,1,1,129},
    {128,0,0,0, 1,1,1,1, 1,1,1,1, 1,1,1,129},
    {128,0,0,0, 0,0,0,0, 0,0,0,0, 1,1,1,129},
    {128,0,0,0, 1,0,0,0, 1,1,1,0, 1,1,1,129},
    {128,1,129,1, 0,0,0,1, 0,0,0,0, 0,0,0,0},
    {128,0,0,0, 0,0,0,0, 129,0,0,0, 1,1,1,0},
    {128,1,129,1, 0,0,1,1, 0,0,0,1, 0,0,0,0},
    {128,0,129,1, 0,0,0,1, 0,0,0,0, 0,0,0,0},
    {128,0,0,0, 1,0,0,0, 129,1,0,0, 1,1,1,0},
    {128,0,0,0, 0,0,0,0, 129,0,0,0, 1,1,0,0},
    {128,1,1,1, 0,0,1,1, 0,0,1,1, 0,0,0,129},
    {128,0,129,1, 0,0,0,1, 0,0,0,1, 0,0,0,0},
    {128,0,0,0, 1,0,0,0, 129,0,0,0, 1,1,0,0},
    {128,1,129,0, 0,1,1,0, 0,1,1,0, 0,1,1,0},
    {128,0,129,1, 0,1,1,0, 0,1,1,0, 1,1,0,0},
    {128,0,0,1, 0,1,1,1, 129,1,1,0, 1,0,0,0},
    {128,0,0,0, 1,1,1,1, 129,1,1,1, 0,0,0,0},
    {128,1,129,1, 0,0,0,1, 1,0,0,0, 1,1,1,0},
    {128,0,129,1, 1,0,0,1, 1,0,0,1, 1,1,0,0},
  };

  private static readonly byte[] _Weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
  private static readonly byte[] _Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

  /// <summary>Decodes one 4×4 block to 16 RGB half-float pixels (96 bytes).</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output, bool isSigned) {
    if (block.Length < _BLOCK_BYTES)
      throw new ArgumentException("A BC6H block is exactly 16 bytes.", nameof(block));
    if (output.Length < _BLOCK_DIM * _BLOCK_DIM * _BYTES_PER_PIXEL)
      throw new ArgumentException("A decoded BC6H block needs 96 bytes of RGB half-float output.", nameof(output));

    var reader = new BitReader(block);
    Span<int> r = stackalloc int[4];
    Span<int> g = stackalloc int[4];
    Span<int> b = stackalloc int[4];

    var modeCode = reader.ReadBits(2);
    if (modeCode > 1)
      modeCode |= reader.ReadBits(3) << 2;

    var partition = 0;
    int mode;

    switch (modeCode) {
      case 0b00:
        g[2] |= reader.ReadBit() << 4;
        b[2] |= reader.ReadBit() << 4;
        b[3] |= reader.ReadBit() << 4;
        r[0] |= reader.ReadBits(10);
        g[0] |= reader.ReadBits(10);
        b[0] |= reader.ReadBits(10);
        r[1] |= reader.ReadBits(5);
        g[3] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 0;
        break;

      case 0b01:
        g[2] |= reader.ReadBit() << 5;
        g[3] |= reader.ReadBit() << 4;
        g[3] |= reader.ReadBit() << 5;
        r[0] |= reader.ReadBits(7);
        b[3] |= reader.ReadBit();
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(7);
        b[2] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 2;
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(7);
        b[3] |= reader.ReadBit() << 3;
        b[3] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(6);
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(6);
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(6);
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(6);
        r[3] |= reader.ReadBits(6);
        partition = reader.ReadBits(5);
        mode = 1;
        break;

      case 0b00010:
        r[0] |= reader.ReadBits(10);
        g[0] |= reader.ReadBits(10);
        b[0] |= reader.ReadBits(10);
        r[1] |= reader.ReadBits(5);
        r[0] |= reader.ReadBit() << 10;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(4);
        g[0] |= reader.ReadBit() << 10;
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(4);
        b[0] |= reader.ReadBit() << 10;
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 2;
        break;

      case 0b00110:
        r[0] |= reader.ReadBits(10);
        g[0] |= reader.ReadBits(10);
        b[0] |= reader.ReadBits(10);
        r[1] |= reader.ReadBits(4);
        r[0] |= reader.ReadBit() << 10;
        g[3] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(5);
        g[0] |= reader.ReadBit() << 10;
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(4);
        b[0] |= reader.ReadBit() << 10;
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(4);
        b[3] |= reader.ReadBit();
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(4);
        g[2] |= reader.ReadBit() << 4;
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 3;
        break;

      case 0b01010:
        r[0] |= reader.ReadBits(10);
        g[0] |= reader.ReadBits(10);
        b[0] |= reader.ReadBits(10);
        r[1] |= reader.ReadBits(4);
        r[0] |= reader.ReadBit() << 10;
        b[2] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(4);
        g[0] |= reader.ReadBit() << 10;
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(5);
        b[0] |= reader.ReadBit() << 10;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(4);
        b[3] |= reader.ReadBit() << 1;
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(4);
        b[3] |= reader.ReadBit() << 4;
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 4;
        break;

      case 0b01110:
        r[0] |= reader.ReadBits(9);
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(9);
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(9);
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(5);
        g[3] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 5;
        break;

      case 0b10010:
        r[0] |= reader.ReadBits(8);
        g[3] |= reader.ReadBit() << 4;
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(8);
        b[3] |= reader.ReadBit() << 2;
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(8);
        b[3] |= reader.ReadBit() << 3;
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(6);
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(6);
        r[3] |= reader.ReadBits(6);
        partition = reader.ReadBits(5);
        mode = 6;
        break;

      case 0b10110:
        r[0] |= reader.ReadBits(8);
        b[3] |= reader.ReadBit();
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(8);
        g[2] |= reader.ReadBit() << 5;
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(8);
        g[3] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(5);
        g[3] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(6);
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 7;
        break;

      case 0b11010:
        r[0] |= reader.ReadBits(8);
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(8);
        b[2] |= reader.ReadBit() << 5;
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(8);
        b[3] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(5);
        g[3] |= reader.ReadBit() << 4;
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit();
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(6);
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 2;
        r[3] |= reader.ReadBits(5);
        b[3] |= reader.ReadBit() << 3;
        partition = reader.ReadBits(5);
        mode = 8;
        break;

      case 0b11110:
        r[0] |= reader.ReadBits(6);
        g[3] |= reader.ReadBit() << 4;
        b[3] |= reader.ReadBit();
        b[3] |= reader.ReadBit() << 1;
        b[2] |= reader.ReadBit() << 4;
        g[0] |= reader.ReadBits(6);
        g[2] |= reader.ReadBit() << 5;
        b[2] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 2;
        g[2] |= reader.ReadBit() << 4;
        b[0] |= reader.ReadBits(6);
        g[3] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 3;
        b[3] |= reader.ReadBit() << 5;
        b[3] |= reader.ReadBit() << 4;
        r[1] |= reader.ReadBits(6);
        g[2] |= reader.ReadBits(4);
        g[1] |= reader.ReadBits(6);
        g[3] |= reader.ReadBits(4);
        b[1] |= reader.ReadBits(6);
        b[2] |= reader.ReadBits(4);
        r[2] |= reader.ReadBits(6);
        r[3] |= reader.ReadBits(6);
        partition = reader.ReadBits(5);
        mode = 9;
        break;

      case 0b00011:
        r[0] = reader.ReadBits(10);
        g[0] = reader.ReadBits(10);
        b[0] = reader.ReadBits(10);
        r[1] = reader.ReadBits(10);
        g[1] = reader.ReadBits(10);
        b[1] = reader.ReadBits(10);
        mode = 10;
        break;

      case 0b00111:
        r[0] = reader.ReadBits(10);
        g[0] = reader.ReadBits(10);
        b[0] = reader.ReadBits(10);
        r[1] = reader.ReadBits(9);
        r[0] |= reader.ReadBit() << 10;
        g[1] = reader.ReadBits(9);
        g[0] |= reader.ReadBit() << 10;
        b[1] = reader.ReadBits(9);
        b[0] |= reader.ReadBit() << 10;
        mode = 11;
        break;

      case 0b01011:
        r[0] = reader.ReadBits(10);
        g[0] = reader.ReadBits(10);
        b[0] = reader.ReadBits(10);
        r[1] = reader.ReadBits(8);
        r[0] |= reader.ReadBitsReversed(2) << 10;
        g[1] = reader.ReadBits(8);
        g[0] |= reader.ReadBitsReversed(2) << 10;
        b[1] = reader.ReadBits(8);
        b[0] |= reader.ReadBitsReversed(2) << 10;
        mode = 12;
        break;

      case 0b01111:
        r[0] = reader.ReadBits(10);
        g[0] = reader.ReadBits(10);
        b[0] = reader.ReadBits(10);
        r[1] = reader.ReadBits(4);
        r[0] |= reader.ReadBitsReversed(6) << 10;
        g[1] = reader.ReadBits(4);
        g[0] |= reader.ReadBitsReversed(6) << 10;
        b[1] = reader.ReadBits(4);
        b[0] |= reader.ReadBitsReversed(6) << 10;
        mode = 13;
        break;

      default:
        output[..(_BLOCK_DIM * _BLOCK_DIM * _BYTES_PER_PIXEL)].Clear();
        return;
    }

    var twoSubset = mode < 10;
    var endpointCount = twoSubset ? 4 : 2;
    var endpointBits = _ActualBits[0, mode];

    if (isSigned) {
      r[0] = _ExtendSign(r[0], endpointBits);
      g[0] = _ExtendSign(g[0], endpointBits);
      b[0] = _ExtendSign(b[0], endpointBits);
    }

    // Modes 10 and 11 in the specification (indices 9 and 10 here) store endpoints directly.
    if ((mode != 9 && mode != 10) || isSigned)
      for (var i = 1; i < endpointCount; ++i) {
        r[i] = _ExtendSign(r[i], _ActualBits[1, mode]);
        g[i] = _ExtendSign(g[i], _ActualBits[2, mode]);
        b[i] = _ExtendSign(b[i], _ActualBits[3, mode]);
      }

    if (mode != 9 && mode != 10)
      for (var i = 1; i < endpointCount; ++i) {
        r[i] = _TransformInverse(r[i], r[0], endpointBits, isSigned);
        g[i] = _TransformInverse(g[i], g[0], endpointBits, isSigned);
        b[i] = _TransformInverse(b[i], b[0], endpointBits, isSigned);
      }

    for (var i = 0; i < endpointCount; ++i) {
      r[i] = _Unquantize(r[i], endpointBits, isSigned);
      g[i] = _Unquantize(g[i], endpointBits, isSigned);
      b[i] = _Unquantize(b[i], endpointBits, isSigned);
    }

    var weights = twoSubset ? _Weights3 : _Weights4;
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var partitionSet = twoSubset ? _Partitions[partition, y * 4 + x] : (byte)((x | y) == 0 ? 128 : 0);
        var indexBits = twoSubset ? 3 : 4;
        if ((partitionSet & 0x80) != 0)
          --indexBits;

        var subset = partitionSet & 1;
        var index = reader.ReadBits(indexBits);
        var ep = subset * 2;
        var at = (y * 4 + x) * _BYTES_PER_PIXEL;
        _WriteU16(output, at, _FinishUnquantize(_Interpolate(r[ep], r[ep + 1], weights, index), isSigned));
        _WriteU16(output, at + 2, _FinishUnquantize(_Interpolate(g[ep], g[ep + 1], weights, index), isSigned));
        _WriteU16(output, at + 4, _FinishUnquantize(_Interpolate(b[ep], b[ep + 1], weights, index), isSigned));
      }
  }

  /// <summary>Decodes a BC6H image to tightly packed <c>RgbF16</c> bytes.</summary>
  public static void DecodeImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output, bool isSigned) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height));

    var expectedOutput = checked(width * height * _BYTES_PER_PIXEL);
    if (output.Length < expectedOutput)
      throw new ArgumentException($"The output needs at least {expectedOutput} bytes.", nameof(output));

    var blocksX = (width + 3) >> 2;
    var blocksY = (height + 3) >> 2;
    var expectedInput = checked(blocksX * blocksY * _BLOCK_BYTES);
    if (data.Length < expectedInput)
      throw new ArgumentException($"The BC6H image needs {expectedInput} bytes but only {data.Length} were supplied.", nameof(data));

    Span<byte> decoded = stackalloc byte[4 * 4 * _BYTES_PER_PIXEL];
    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockAt = (by * blocksX + bx) * _BLOCK_BYTES;
        DecodeBlock(data.Slice(blockAt, _BLOCK_BYTES), decoded, isSigned);

        for (var py = 0; py < 4; ++py) {
          var y = by * 4 + py;
          if (y >= height)
            break;

          for (var px = 0; px < 4; ++px) {
            var x = bx * 4 + px;
            if (x >= width)
              break;

            var from = (py * 4 + px) * _BYTES_PER_PIXEL;
            var to = (y * width + x) * _BYTES_PER_PIXEL;
            decoded.Slice(from, _BYTES_PER_PIXEL).CopyTo(output.Slice(to, _BYTES_PER_PIXEL));
          }
        }
      }
  }

  private static int _ExtendSign(int value, int bits) {
    var shift = 32 - bits;
    return value << shift >> shift;
  }

  private static int _TransformInverse(int value, int first, int bits, bool isSigned) {
    value = (value + first) & ((1 << bits) - 1);
    return isSigned ? _ExtendSign(value, bits) : value;
  }

  private static int _Unquantize(int value, int bits, bool isSigned) {
    if (!isSigned) {
      if (bits >= 15)
        return value;
      if (value == 0)
        return 0;
      if (value == (1 << bits) - 1)
        return 0xFFFF;
      return ((value << 16) + 0x8000) >> bits;
    }

    if (bits >= 16)
      return value;

    var negative = value < 0;
    if (negative)
      value = -value;

    int unquantized;
    if (value == 0)
      unquantized = 0;
    else if (value >= (1 << (bits - 1)) - 1)
      unquantized = 0x7FFF;
    else
      unquantized = ((value << 15) + 0x4000) >> (bits - 1);

    return negative ? -unquantized : unquantized;
  }

  private static int _Interpolate(int a, int b, ReadOnlySpan<byte> weights, int index)
    => (a * (64 - weights[index]) + b * weights[index] + 32) >> 6;

  private static ushort _FinishUnquantize(int value, bool isSigned) {
    if (!isSigned)
      return (ushort)((value * 31) >> 6);

    value = value < 0 ? -((-value * 31) >> 5) : value * 31 >> 5;
    var sign = 0;
    if (value < 0) {
      sign = 0x8000;
      value = -value;
    }

    return (ushort)(sign | value);
  }

  private static void _WriteU16(Span<byte> output, int at, ushort value) {
    output[at] = (byte)value;
    output[at + 1] = (byte)(value >> 8);
  }

  private ref struct BitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _bit;

    internal BitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._bit = 0;
    }

    internal int ReadBit() => this.ReadBits(1);

    internal int ReadBits(int count) {
      var result = 0;
      for (var i = 0; i < count; ++i) {
        var at = this._bit++;
        result |= ((this._data[at >> 3] >> (at & 7)) & 1) << i;
      }
      return result;
    }

    internal int ReadBitsReversed(int count) {
      var value = this.ReadBits(count);
      var reversed = 0;
      while (count-- > 0) {
        reversed = (reversed << 1) | (value & 1);
        value >>= 1;
      }
      return reversed;
    }
  }
}
