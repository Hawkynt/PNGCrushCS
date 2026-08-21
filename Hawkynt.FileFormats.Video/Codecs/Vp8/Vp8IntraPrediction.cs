using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Predicts a macroblock from the already-reconstructed parts of the frame around it
/// (RFC 6386, 12).
/// </summary>
/// <remarks>
/// Everything here reads the reconstruction and not the loop-filtered picture. The loop filter runs
/// once the whole frame is built precisely so that this stage sees the unfiltered samples; a decoder
/// that filtered as it went would predict from pixels the encoder never predicted from, and the
/// error would be small, everywhere, and would grow with every frame until the next key frame.
/// <para/>
/// Where a predictor lies outside the picture it takes a fixed value: 127 above the top row and 129
/// to the left of the leftmost column, with the corner above and to the left of the first sample
/// counting as "above" and so as 127. DC prediction is the exception — it does not use those values
/// but averages only the samples that exist, and averages nothing at all in the top-left macroblock,
/// where it fills with 128.
/// </remarks>
internal static class Vp8IntraPrediction {

  private const byte _ABOVE_THE_PICTURE = 127;
  private const byte _LEFT_OF_THE_PICTURE = 129;

  /// <summary>
  /// Collects the samples above a block: the one diagonally above-left, the row itself, and however
  /// many above and to the right the caller needs.
  /// </summary>
  /// <param name="edge">
  /// Receives <paramref name="count"/> + <paramref name="extra"/> + 1 samples, the first being the
  /// one above and to the left.
  /// </param>
  internal static void GatherAbove(
    byte[] plane, int stride, int planeWidth, int x, int y, int count, int extra, Span<byte> edge) {
    if (y == 0) {
      edge[..(count + extra + 1)].Fill(_ABOVE_THE_PICTURE);
      return;
    }

    var row = (y - 1) * stride;
    edge[0] = x == 0 ? _LEFT_OF_THE_PICTURE : plane[row + x - 1];

    for (var i = 0; i < count; ++i)
      edge[1 + i] = plane[row + x + i];

    // Past the right-hand end of the picture the last sample of the row above stands in for all of
    // them, which is what the border of replicated samples in other decoders amounts to and what RFC
    // 6386 section 12.3 asks for by name.
    var last = planeWidth - 1;
    for (var i = 0; i < extra; ++i) {
      var at = x + count + i;
      edge[1 + count + i] = plane[row + (at > last ? last : at)];
    }
  }

  /// <summary>Collects the samples immediately to the left of a block.</summary>
  internal static void GatherLeft(byte[] plane, int stride, int x, int y, int count, Span<byte> left) {
    if (x == 0) {
      left[..count].Fill(_LEFT_OF_THE_PICTURE);
      return;
    }

    for (var i = 0; i < count; ++i)
      left[i] = plane[(y + i) * stride + x - 1];
  }

  /// <summary>
  /// Fills a whole 16x16 luma or 8x8 chroma block with one of the four full-block modes
  /// (RFC 6386, 12.2 and 12.3).
  /// </summary>
  /// <param name="size">16 for luma, 8 for chroma.</param>
  /// <param name="hasAbove">Whether there is a macroblock row above this one.</param>
  /// <param name="hasLeft">Whether there is a macroblock to the left.</param>
  internal static void PredictBlock(
    byte[] plane, int stride, int x, int y, int size, int mode,
    ReadOnlySpan<byte> edge, ReadOnlySpan<byte> left, bool hasAbove, bool hasLeft) {
    var above = edge[1..];

    switch (mode) {
      case Vp8Mode.VERTICAL_PREDICTION:
        for (var row = 0; row < size; ++row)
          above[..size].CopyTo(plane.AsSpan((y + row) * stride + x, size));

        return;

      case Vp8Mode.HORIZONTAL_PREDICTION:
        for (var row = 0; row < size; ++row)
          plane.AsSpan((y + row) * stride + x, size).Fill(left[row]);

        return;

      case Vp8Mode.TRUE_MOTION_PREDICTION: {
        int corner = edge[0];
        for (var row = 0; row < size; ++row) {
          var at = (y + row) * stride + x;
          int leftSample = left[row];
          for (var column = 0; column < size; ++column)
            plane[at + column] = _Clamp(leftSample + above[column] - corner);
        }

        return;
      }

      default: {
        var value = _DirectCurrentValue(above, left, size, hasAbove, hasLeft);
        for (var row = 0; row < size; ++row)
          plane.AsSpan((y + row) * stride + x, size).Fill(value);

        return;
      }
    }
  }

  /// <summary>
  /// The single value a DC-predicted block is filled with, averaged over whichever of its neighbours
  /// exist (RFC 6386, 12.2).
  /// </summary>
  /// <remarks>
  /// The out-of-picture values of 127 and 129 deliberately take no part. Averaging them in would be
  /// a defensible reading of "the row above" and gives a different, wrong, number for every
  /// macroblock along two edges of every key frame.
  /// </remarks>
  private static byte _DirectCurrentValue(
    ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, int size, bool hasAbove, bool hasLeft) {
    if (!hasAbove && !hasLeft)
      return 128;

    var sum = 0;
    var shift = size == 16 ? 4 : 3;

    if (hasAbove)
      for (var i = 0; i < size; ++i)
        sum += above[i];

    if (hasLeft)
      for (var i = 0; i < size; ++i)
        sum += left[i];

    if (hasAbove && hasLeft)
      ++shift;

    return (byte)((sum + (1 << (shift - 1))) >> shift);
  }

  /// <summary>
  /// Predicts one 4x4 luma subblock into the plane (RFC 6386, 12.3).
  /// </summary>
  /// <param name="edge">
  /// Nine samples: the four to the left in reverse order, the one diagonally above-left, and the
  /// four above — the arrangement the four diagonal modes walk along.
  /// </param>
  /// <param name="above">Eight samples: the four above the subblock and the four above and to its right.</param>
  /// <param name="left">The four samples to the left of the subblock, top to bottom.</param>
  internal static void PredictSubblock(
    byte[] plane, int stride, int x, int y, int mode,
    ReadOnlySpan<byte> edge, ReadOnlySpan<byte> above, ReadOnlySpan<byte> left) {
    Span<byte> block = stackalloc byte[16];

    switch (mode) {
      case Vp8Mode.B_DC_PREDICTION: {
        var sum = 4;
        for (var i = 0; i < 4; ++i)
          sum += above[i] + left[i];

        block.Fill((byte)(sum >> 3));
        break;
      }

      case Vp8Mode.B_TRUE_MOTION_PREDICTION: {
        int corner = edge[4];
        for (var row = 0; row < 4; ++row)
          for (var column = 0; column < 4; ++column)
            block[row * 4 + column] = _Clamp(left[row] + above[column] - corner);

        break;
      }

      case Vp8Mode.B_VERTICAL_PREDICTION:
        for (var column = 0; column < 4; ++column) {
          var value = _AverageOfThree(
            column == 0 ? edge[4] : above[column - 1], above[column], above[column + 1]);
          block[column] = block[4 + column] = block[8 + column] = block[12 + column] = value;
        }

        break;

      case Vp8Mode.B_HORIZONTAL_PREDICTION: {
        for (var row = 0; row < 3; ++row) {
          var value = _AverageOfThree(row == 0 ? edge[4] : left[row - 1], left[row], left[row + 1]);
          block[row * 4] = block[row * 4 + 1] = block[row * 4 + 2] = block[row * 4 + 3] = value;
        }

        var bottom = _AverageOfThree(left[2], left[3], left[3]);
        block[12] = block[13] = block[14] = block[15] = bottom;
        break;
      }

      case Vp8Mode.B_LEFT_DOWN_PREDICTION:
        block[0] = _SmoothedAt(above, 1);
        block[1] = block[4] = _SmoothedAt(above, 2);
        block[2] = block[5] = block[8] = _SmoothedAt(above, 3);
        block[3] = block[6] = block[9] = block[12] = _SmoothedAt(above, 4);
        block[7] = block[10] = block[13] = _SmoothedAt(above, 5);
        block[11] = block[14] = _SmoothedAt(above, 6);
        block[15] = _AverageOfThree(above[6], above[7], above[7]);
        break;

      case Vp8Mode.B_RIGHT_DOWN_PREDICTION:
        block[12] = _SmoothedAt(edge, 1);
        block[13] = block[8] = _SmoothedAt(edge, 2);
        block[14] = block[9] = block[4] = _SmoothedAt(edge, 3);
        block[15] = block[10] = block[5] = block[0] = _SmoothedAt(edge, 4);
        block[11] = block[6] = block[1] = _SmoothedAt(edge, 5);
        block[7] = block[2] = _SmoothedAt(edge, 6);
        block[3] = _SmoothedAt(edge, 7);
        break;

      case Vp8Mode.B_VERTICAL_RIGHT_PREDICTION:
        block[12] = _SmoothedAt(edge, 2);
        block[8] = _SmoothedAt(edge, 3);
        block[13] = block[4] = _SmoothedAt(edge, 4);
        block[9] = block[0] = _AverageOfTwoAt(edge, 4);
        block[14] = block[5] = _SmoothedAt(edge, 5);
        block[10] = block[1] = _AverageOfTwoAt(edge, 5);
        block[15] = block[6] = _SmoothedAt(edge, 6);
        block[11] = block[2] = _AverageOfTwoAt(edge, 6);
        block[7] = _SmoothedAt(edge, 7);
        block[3] = _AverageOfTwoAt(edge, 7);
        break;

      case Vp8Mode.B_VERTICAL_LEFT_PREDICTION:
        block[0] = _AverageOfTwo(above[0], above[1]);
        block[4] = _SmoothedAt(above, 1);
        block[8] = block[1] = _AverageOfTwo(above[1], above[2]);
        block[5] = block[12] = _SmoothedAt(above, 2);
        block[9] = block[2] = _AverageOfTwo(above[2], above[3]);
        block[13] = block[6] = _SmoothedAt(above, 3);
        block[10] = block[3] = _AverageOfTwo(above[3], above[4]);
        block[14] = block[7] = _SmoothedAt(above, 4);
        block[11] = _SmoothedAt(above, 5);
        block[15] = _SmoothedAt(above, 6);
        break;

      case Vp8Mode.B_HORIZONTAL_DOWN_PREDICTION:
        block[12] = _AverageOfTwo(edge[0], edge[1]);
        block[13] = _SmoothedAt(edge, 1);
        block[8] = block[14] = _AverageOfTwo(edge[1], edge[2]);
        block[9] = block[15] = _SmoothedAt(edge, 2);
        block[10] = block[4] = _AverageOfTwo(edge[2], edge[3]);
        block[11] = block[5] = _SmoothedAt(edge, 3);
        block[6] = block[0] = _AverageOfTwo(edge[3], edge[4]);
        block[7] = block[1] = _SmoothedAt(edge, 4);
        block[2] = _SmoothedAt(edge, 5);
        block[3] = _SmoothedAt(edge, 6);
        break;

      default:
        block[0] = _AverageOfTwo(left[0], left[1]);
        block[1] = _SmoothedAt(left, 1);
        block[2] = block[4] = _AverageOfTwo(left[1], left[2]);
        block[3] = block[5] = _SmoothedAt(left, 2);
        block[6] = block[8] = _AverageOfTwo(left[2], left[3]);
        block[7] = block[9] = _AverageOfThree(left[2], left[3], left[3]);
        block[10] = block[11] = block[12] = block[13] = block[14] = block[15] = left[3];
        break;
    }

    for (var row = 0; row < 4; ++row)
      block.Slice(row * 4, 4).CopyTo(plane.AsSpan((y + row) * stride + x, 4));
  }

  /// <summary>The weighted average of three neighbouring samples, centred on the middle one.</summary>
  private static byte _AverageOfThree(int before, int centre, int after) => (byte)((before + centre + centre + after + 2) >> 2);

  /// <summary>The same, given the position of the middle sample.</summary>
  private static byte _SmoothedAt(ReadOnlySpan<byte> samples, int at) => _AverageOfThree(samples[at - 1], samples[at], samples[at + 1]);

  /// <summary>The average of two adjacent samples, which stands for the sample half a step between them.</summary>
  private static byte _AverageOfTwo(int first, int second) => (byte)((first + second + 1) >> 1);

  /// <summary>The same, given the position of the first of the two.</summary>
  private static byte _AverageOfTwoAt(ReadOnlySpan<byte> samples, int at) => _AverageOfTwo(samples[at], samples[at + 1]);

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
