using System;
using System.IO;

namespace FileFormat.Eri;

/// <summary>Decodes one lossy ERI key frame (ERI version 28H, the ERISA library's lossy format) coded with the DCT.</summary>
/// <remarks>
/// The picture is converted to Y, U, V (and opacity), cut into 16×16 macroblocks of four 8×8 blocks
/// per channel — or one per chroma channel when the chroma is sampled 4:1:1 — and each block is
/// transformed by an 8×8 DCT. Coefficients are quantised by one 8×8 table for luminance and one for
/// chroma, scaled per macroblock by a code whose low bit picks 1 or 1.5 and whose upper bits are a
/// power of two, and sent in zigzag order, interleaved coefficient by coefficient across the
/// macroblock's blocks, coded with the order-0 ERINA code. The tables use an order-0 Huffman code
/// of their own and the scale codes the run-length gamma code. Within a channel, the DC of the
/// second to fourth block is sent as its difference from the first's.
/// <para/>
/// Rounding is to nearest, ties to even, which is what the shipped library's FPU code does.
/// </remarks>
internal static class EriLossyDecoder {

  private const int _SIZE = 8;
  private const int _AREA = 64;
  private const int _MACRO = 16;
  private const uint _SAMPLING_444 = 0x00040404;
  private const uint _SAMPLING_411 = 0x00040101;

  /// <summary>The 8-point inverse DCT the library uses, unnormalised: x[n] = X[0] + Σ X[k]·cos((2n+1)kπ/16).</summary>
  private static readonly float[] _Basis = _MakeBasis();

  private static readonly int[] _ZigZag = _MakeZigZag();

  private static float[] _MakeBasis() {
    var basis = new float[_AREA];
    for (var n = 0; n < _SIZE; ++n)
      for (var k = 0; k < _SIZE; ++k)
        basis[n * _SIZE + k] = k == 0 ? 1.0F : (float)Math.Cos(Math.PI * (2 * n + 1) * k / 16.0);
    return basis;
  }

  // The usual zigzag: first step to the right, then down the anti-diagonals and back up.
  private static int[] _MakeZigZag() {
    var table = new int[_AREA];
    var i = 0;
    for (var sum = 0; sum < 2 * _SIZE - 1; ++sum)
      for (var step = 0; step <= sum; ++step) {
        var along = (sum & 1) == 0 ? step : sum - step;
        var x = along;
        var y = sum - along;
        if (x < _SIZE && y < _SIZE)
          table[i++] = y * _SIZE + x;
      }
    return table;
  }

  /// <summary>Decodes a key frame into <paramref name="samples"/>: rows in stored order, B G R (A) or grey.</summary>
  public static void Decode(byte[] record, in EriInfoHeader info, byte[] samples, int channels) {
    if (record.Length < 4)
      throw new InvalidDataException("ERI image record is too short to hold its header.");

    var version = record[0];
    var operations = record[1];
    var encodeType = record[2];
    if (version == 9)
      throw new NotSupportedException("ERI lossy format version 9 (the ERINA library's, withdrawn by the specification) is not read.");
    if (version != 0x28)
      throw new NotSupportedException($"ERI lossy image coding version {version:X2} is not one the format defines.");
    if (record[3] != 8)
      throw new InvalidDataException("ERI lossy image header does not state 8-bit symbols.");
    if ((operations & 0x03) != 0)
      throw new NotSupportedException("ERI lossy frames predicted by motion vectors are not read.");
    if (info.BlockingDegree != 3)
      throw new InvalidDataException("ERI lossy picture must be blocked 8×8.");
    if (info.Architecture != EriInfoHeader.RunLengthGamma)
      throw new InvalidDataException("ERI lossy picture must name the run-length gamma architecture.");

    // A frame may ask for the DCT whatever the header says; a LOT picture that does not is refused.
    var transformation = encodeType == 1 ? EriInfoHeader.DctTransformation : info.Transformation;
    if (transformation != EriInfoHeader.DctTransformation)
      throw new NotSupportedException("ERI lossy pictures in the lapped transform (LOT) are not read: the Entis library's own decoder does not give back what its encoder was given, so no decode can be verified.");

    var fourOneOne = info.SamplingFlags switch {
      _SAMPLING_444 => false,
      _SAMPLING_411 => true,
      _ => throw new NotSupportedException($"ERI lossy chroma sampling {info.SamplingFlags:X8} is not read (4:2:2 is withdrawn by the specification)."),
    };

    var blocksets = fourOneOne ? channels switch { 1 => 4, 3 => 6, _ => 10 } : channels * 4;
    var width = info.Width;
    var rows = info.Rows;
    var widthBlocks = (width + _MACRO - 1) / _MACRO;
    var heightBlocks = (rows + _MACRO - 1) / _MACRO;

    var bits = new EriBitReader(record, 4);
    if (bits.ReadBit() != 0)
      throw new InvalidDataException("ERI reserved bit before the image code is set.");

    var coefficients = new EriHuffmanDecoder(bits, orderZero: true);
    var tableTree = new EriHuffmanTree();
    var quantiser = new byte[_AREA * 2];
    for (var i = 0; i < quantiser.Length; ++i)
      quantiser[i] = (byte)coefficients.Symbol(tableTree);

    var gamma = new EriGammaDecoder(bits);
    gamma.Reset();
    var scales = new sbyte[widthBlocks * heightBlocks * 2];
    gamma.Decode(scales);

    var yuvPixel = channels == 1 ? 1 : 4;
    var yuvStride = widthBlocks * _MACRO * yuvPixel;
    var yuv = new sbyte[yuvStride * heightBlocks * _MACRO];

    var blocks = new float[blocksets][];
    for (var i = 0; i < blocksets; ++i)
      blocks[i] = new float[_AREA];
    var levels = new sbyte[widthBlocks * _AREA * blocksets];
    var parameters = new[] { new float[_AREA], new float[_AREA] };
    var temp = new float[_AREA];
    var pixel = new sbyte[_AREA];
    var ordered = new sbyte[_AREA * blocksets];

    var scaleIndex = 0;
    for (var by = 0; by < heightBlocks; ++by) {
      coefficients.Decode(levels);
      for (var bx = 0; bx < widthBlocks; ++bx) {
        _Dequantise(levels.AsSpan(bx * _AREA * blocksets, _AREA * blocksets), ordered,
          scales[scaleIndex], scales[scaleIndex + 1], quantiser, parameters, blocks, fourOneOne, channels, blocksets);
        scaleIndex += 2;
        foreach (var block in blocks)
          _InverseDct(block, temp);
        _Store(blocks, bx, by, fourOneOne, channels, yuv, yuvStride, yuvPixel, pixel);
      }
    }

    _ToRgb(yuv, yuvStride, yuvPixel, width, rows, channels, samples);
  }

  private static void _Dequantise(ReadOnlySpan<sbyte> sent, sbyte[] ordered, sbyte lumaScale, sbyte chromaScale,
    byte[] table, float[][] parameters, float[][] blocks, bool fourOneOne, int channels, int blocksets) {
    const float matrixScale = (float)(2.0 / _SIZE);
    Span<sbyte> scale = [lumaScale, chromaScale];
    for (var i = 0; i < 2; ++i) {
      // The low bit picks 1 or 1.5; the rest is a power of two, rounded down also when negative.
      var factor = (scale[i] & 1) != 0 ? 1.5F : 1.0F;
      factor *= (float)Math.Pow(2.0, scale[i] >> 1);
      factor *= matrixScale;
      for (var j = 0; j < _AREA; ++j)
        parameters[i][j] = factor * (table[i * _AREA + j] + 1);
    }

    for (var i = 0; i < blocksets; ++i)
      for (var j = 0; j < _AREA; ++j)
        ordered[i * _AREA + j] = sent[j * blocksets + i];

    // Luminance, and opacity, have four blocks a macroblock; so does chroma unless it is 4:1:1.
    _UndoDcDifference(ordered, 0);
    var first = fourOneOne ? _AREA * 6 : _AREA * 4;
    for (var c = fourOneOne ? 3 : 1; c < channels; ++c, first += _AREA * 4)
      _UndoDcDifference(ordered, first);

    for (var i = 0; i < blocksets; ++i) {
      var chroma = fourOneOne ? i is 4 or 5 : i is >= 4 and < 12;
      var parameter = parameters[chroma ? 1 : 0];
      var block = blocks[i];
      for (var j = 0; j < _AREA; ++j)
        block[_ZigZag[j]] = ordered[i * _AREA + j] * parameter[j];
    }
  }

  private static void _UndoDcDifference(sbyte[] ordered, int first) {
    for (var b = 1; b < 4; ++b)
      ordered[first + b * _AREA] += ordered[first];
  }

  private static void _InverseDct(float[] block, float[] temp) {
    // Down the columns first, then along the rows.
    for (var column = 0; column < _SIZE; ++column)
      for (var n = 0; n < _SIZE; ++n) {
        var sum = 0.0F;
        for (var k = 0; k < _SIZE; ++k)
          sum += _Basis[n * _SIZE + k] * block[k * _SIZE + column];
        temp[n * _SIZE + column] = sum;
      }

    for (var row = 0; row < _SIZE; ++row)
      for (var n = 0; n < _SIZE; ++n) {
        var sum = 0.0F;
        for (var k = 0; k < _SIZE; ++k)
          sum += _Basis[n * _SIZE + k] * temp[row * _SIZE + k];
        block[row * _SIZE + n] = sum;
      }
  }

  private static int _Round(float value) => (int)MathF.Round(value, MidpointRounding.ToEven);

  private static void _ToUnsigned(float[] block, Span<sbyte> target) {
    for (var i = 0; i < _AREA; ++i)
      target[i] = (sbyte)(byte)Math.Clamp(_Round(block[i]), 0, 255);
  }

  private static void _ToSigned(float[] block, Span<sbyte> target) {
    for (var i = 0; i < _AREA; ++i)
      target[i] = (sbyte)Math.Clamp(_Round(block[i]), -128, 127);
  }

  private static void _Put(ReadOnlySpan<sbyte> pixel, int xBlock, int yBlock, int channel, sbyte[] yuv, int stride, int pixelBytes) {
    var at = yBlock * _SIZE * stride + xBlock * _SIZE * pixelBytes + channel;
    for (var y = 0; y < _SIZE; ++y, at += stride)
      for (var x = 0; x < _SIZE; ++x)
        yuv[at + x * pixelBytes] = pixel[y * _SIZE + x];
  }

  private static void _PutDoubled(ReadOnlySpan<sbyte> pixel, int xMacro, int yMacro, int channel, sbyte[] yuv, int stride, int pixelBytes) {
    var at = yMacro * _MACRO * stride + xMacro * _MACRO * pixelBytes + channel;
    for (var y = 0; y < _SIZE; ++y, at += stride * 2)
      for (var x = 0; x < _SIZE; ++x) {
        var d = pixel[y * _SIZE + x];
        var p = at + x * 2 * pixelBytes;
        yuv[p] = yuv[p + pixelBytes] = yuv[p + stride] = yuv[p + stride + pixelBytes] = d;
      }
  }

  private static void _Store(float[][] blocks, int x, int y, bool fourOneOne, int channels,
    sbyte[] yuv, int stride, int pixelBytes, sbyte[] pixel) {
    for (var i = 0; i < 2; ++i)
      for (var j = 0; j < 2; ++j) {
        var (xBlock, yBlock, k) = (x * 2 + j, y * 2 + i, i * 2 + j);
        _ToUnsigned(blocks[k], pixel);
        _Put(pixel, xBlock, yBlock, 0, yuv, stride, pixelBytes);

        if (!fourOneOne && channels >= 3) {
          _ToSigned(blocks[k + 4], pixel);
          _Put(pixel, xBlock, yBlock, 1, yuv, stride, pixelBytes);
          _ToSigned(blocks[k + 8], pixel);
          _Put(pixel, xBlock, yBlock, 2, yuv, stride, pixelBytes);
        }

        if (channels >= 4) {
          _ToUnsigned(blocks[k + (fourOneOne ? 6 : 12)], pixel);
          _Put(pixel, xBlock, yBlock, 3, yuv, stride, pixelBytes);
        }
      }

    if (!fourOneOne || channels < 3)
      return;

    _ToSigned(blocks[4], pixel);
    _PutDoubled(pixel, x, y, 1, yuv, stride, pixelBytes);
    _ToSigned(blocks[5], pixel);
    _PutDoubled(pixel, x, y, 2, yuv, stride, pixelBytes);
  }

  private static int _Clamp(int value) => Math.Clamp(value, 0, 255);

  private static void _ToRgb(sbyte[] yuv, int stride, int pixelBytes, int width, int rows, int channels, byte[] samples) {
    for (var y = 0; y < rows; ++y)
      for (var x = 0; x < width; ++x) {
        var at = y * stride + x * pixelBytes;
        var target = (y * width + x) * channels;
        if (channels == 1) {
          samples[target] = (byte)yuv[at];
          continue;
        }

        int luma = (byte)yuv[at];
        int u = yuv[at + 1];
        int v = yuv[at + 2];
        samples[target] = (byte)_Clamp(luma + ((u * 7) >> 2));
        samples[target + 1] = (byte)_Clamp(luma - ((u * 3 + v * 6) >> 3));
        samples[target + 2] = (byte)_Clamp(luma + ((v * 3) >> 1));
        if (channels == 4)
          samples[target + 3] = (byte)yuv[at + 3];
      }
  }
}
