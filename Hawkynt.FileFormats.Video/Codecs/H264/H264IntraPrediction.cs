using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Predicts a block from the samples already reconstructed above and to the left of it — ITU-T
/// H.264, clause 8.3.
/// </summary>
/// <remarks>
/// Everything here reads only the neighbouring row and column, never the block being predicted, and
/// writes only the prediction. That is what makes intra prediction cheap and it is also what makes it
/// fragile: the neighbours are reconstructed samples, so they carry whatever the blocks before them
/// got wrong, and a prediction mode that reads one sample too far along a row produces a picture that
/// is subtly and everywhere wrong rather than obviously broken in one place.
/// <para/>
/// Which neighbours exist is not a detail. A block at the top of a picture, at the left, at a slice
/// boundary, or beside an inter-coded macroblock in a stream with <c>constrained_intra_pred_flag</c>
/// set has fewer than the full complement, and every mode below either has a defined substitute or is
/// not allowed to be chosen at all. The availability flags are therefore parameters rather than
/// something inferred here: only the caller knows where in the picture and in the slice this block is.
/// </remarks>
internal static class H264IntraPrediction {

  /// <summary>The Intra_4x4 prediction modes of Table 8-2.</summary>
  internal const int VERTICAL_4X4 = 0;
  internal const int HORIZONTAL_4X4 = 1;
  internal const int DC_4X4 = 2;
  internal const int DIAGONAL_DOWN_LEFT_4X4 = 3;
  internal const int DIAGONAL_DOWN_RIGHT_4X4 = 4;
  internal const int VERTICAL_RIGHT_4X4 = 5;
  internal const int HORIZONTAL_DOWN_4X4 = 6;
  internal const int VERTICAL_LEFT_4X4 = 7;
  internal const int HORIZONTAL_UP_4X4 = 8;

  /// <summary>The Intra_16x16 prediction modes of Table 8-3.</summary>
  internal const int VERTICAL_16X16 = 0;
  internal const int HORIZONTAL_16X16 = 1;
  internal const int DC_16X16 = 2;
  internal const int PLANE_16X16 = 3;

  /// <summary>The intra chroma prediction modes of Table 8-5, which are in a different order again.</summary>
  internal const int DC_CHROMA = 0;
  internal const int HORIZONTAL_CHROMA = 1;
  internal const int VERTICAL_CHROMA = 2;
  internal const int PLANE_CHROMA = 3;

  /// <summary>
  /// Predicts one 4x4 luma block — clause 8.3.1.2.
  /// </summary>
  /// <param name="mode">One of the nine modes of Table 8-2.</param>
  /// <param name="top">
  /// <c>p[0..7,−1]</c>: the four samples above and the four above-right, with the above-right already
  /// substituted by the caller where the standard calls for it.
  /// </param>
  /// <param name="left"><c>p[−1,0..3]</c>.</param>
  /// <param name="topLeft"><c>p[−1,−1]</c>.</param>
  /// <param name="pred">Receives the sixteen predicted samples in raster order.</param>
  internal static void Predict4x4(
    int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft,
    bool topAvailable, bool leftAvailable, bool topLeftAvailable, Span<byte> pred) {
    switch (mode) {
      case VERTICAL_4X4:
        _RefuseUnavailable(topAvailable, "Intra_4x4_Vertical", "above");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pred[(y << 2) + x] = top[x];

        return;

      case HORIZONTAL_4X4:
        _RefuseUnavailable(leftAvailable, "Intra_4x4_Horizontal", "to the left");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pred[(y << 2) + x] = left[y];

        return;

      case DC_4X4: {
        // The one mode with a defined answer for every combination of neighbours, which is why it is
        // the mode a block with none is required to use (clause 8.3.1.1).
        int value;
        if (topAvailable && leftAvailable)
          value = (top[0] + top[1] + top[2] + top[3] + left[0] + left[1] + left[2] + left[3] + 4) >> 3;
        else if (leftAvailable)
          value = (left[0] + left[1] + left[2] + left[3] + 2) >> 2;
        else if (topAvailable)
          value = (top[0] + top[1] + top[2] + top[3] + 2) >> 2;
        else
          value = 128;

        pred[..16].Fill((byte)value);
        return;
      }

      case DIAGONAL_DOWN_LEFT_4X4:
        _RefuseUnavailable(topAvailable, "Intra_4x4_Diagonal_Down_Left", "above");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pred[(y << 2) + x] = x == 3 && y == 3
              ? (byte)((top[6] + 3 * top[7] + 2) >> 2)
              : (byte)((top[x + y] + 2 * top[x + y + 1] + top[x + y + 2] + 2) >> 2);

        return;

      case DIAGONAL_DOWN_RIGHT_4X4:
        _RefuseUnavailable(topAvailable && leftAvailable && topLeftAvailable, "Intra_4x4_Diagonal_Down_Right",
          "above, to the left and above-left");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pred[(y << 2) + x] = x > y ? (byte)((_Top(top, topLeft, x - y - 2) + 2 * _Top(top, topLeft, x - y - 1) + top[x - y] + 2) >> 2)
              : x < y ? (byte)((_Left(left, topLeft, y - x - 2) + 2 * _Left(left, topLeft, y - x - 1) + left[y - x] + 2) >> 2)
              : (byte)((top[0] + 2 * topLeft + left[0] + 2) >> 2);

        return;

      case VERTICAL_RIGHT_4X4:
        _RefuseUnavailable(topAvailable && leftAvailable && topLeftAvailable, "Intra_4x4_Vertical_Right",
          "above, to the left and above-left");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            var zVR = 2 * x - y;
            var half = x - (y >> 1);
            pred[(y << 2) + x] = zVR >= 0 && (zVR & 1) == 0
                ? (byte)((_Top(top, topLeft, half - 1) + _Top(top, topLeft, half) + 1) >> 1)
              : zVR >= 0
                ? (byte)((_Top(top, topLeft, half - 2) + 2 * _Top(top, topLeft, half - 1) + _Top(top, topLeft, half) + 2) >> 2)
              : zVR == -1
                ? (byte)((left[0] + 2 * topLeft + top[0] + 2) >> 2)
                : (byte)((_Left(left, topLeft, y - 1) + 2 * _Left(left, topLeft, y - 2) + _Left(left, topLeft, y - 3) + 2) >> 2);
          }

        return;

      case HORIZONTAL_DOWN_4X4:
        _RefuseUnavailable(topAvailable && leftAvailable && topLeftAvailable, "Intra_4x4_Horizontal_Down",
          "above, to the left and above-left");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            var zHD = 2 * y - x;
            var half = y - (x >> 1);
            pred[(y << 2) + x] = zHD >= 0 && (zHD & 1) == 0
                ? (byte)((_Left(left, topLeft, half - 1) + _Left(left, topLeft, half) + 1) >> 1)
              : zHD >= 0
                ? (byte)((_Left(left, topLeft, half - 2) + 2 * _Left(left, topLeft, half - 1) + _Left(left, topLeft, half) + 2) >> 2)
              : zHD == -1
                ? (byte)((left[0] + 2 * topLeft + top[0] + 2) >> 2)
                : (byte)((_Top(top, topLeft, x - 1) + 2 * _Top(top, topLeft, x - 2) + _Top(top, topLeft, x - 3) + 2) >> 2);
          }

        return;

      case VERTICAL_LEFT_4X4:
        _RefuseUnavailable(topAvailable, "Intra_4x4_Vertical_Left", "above");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            var at = x + (y >> 1);
            pred[(y << 2) + x] = (y & 1) == 0
              ? (byte)((top[at] + top[at + 1] + 1) >> 1)
              : (byte)((top[at] + 2 * top[at + 1] + top[at + 2] + 2) >> 2);
          }

        return;

      case HORIZONTAL_UP_4X4:
        _RefuseUnavailable(leftAvailable, "Intra_4x4_Horizontal_Up", "to the left");
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            var zHU = x + 2 * y;
            var at = y + (x >> 1);
            pred[(y << 2) + x] = zHU switch {
              5 => (byte)((left[2] + 3 * left[3] + 2) >> 2),
              > 5 => left[3],
              _ when (zHU & 1) == 0 => (byte)((left[at] + left[at + 1] + 1) >> 1),
              _ => (byte)((left[at] + 2 * left[at + 1] + left[at + 2] + 2) >> 2),
            };
          }

        return;

      default:
        throw new InvalidDataException(
          $"An H.264 macroblock states Intra4x4PredMode {mode}. H.264, Table 8-2 defines 0 to 8 only.");
    }
  }

  /// <summary>
  /// Predicts a whole 16x16 luma macroblock — clause 8.3.3.
  /// </summary>
  /// <param name="top"><c>p[0..15,−1]</c>.</param>
  /// <param name="left"><c>p[−1,0..15]</c>.</param>
  /// <param name="pred">Receives the 256 predicted samples in raster order.</param>
  internal static void Predict16x16(
    int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft,
    bool topAvailable, bool leftAvailable, bool topLeftAvailable, Span<byte> pred) {
    switch (mode) {
      case VERTICAL_16X16:
        _RefuseUnavailable(topAvailable, "Intra_16x16_Vertical", "above");
        for (var y = 0; y < 16; ++y)
          for (var x = 0; x < 16; ++x)
            pred[(y << 4) + x] = top[x];

        return;

      case HORIZONTAL_16X16:
        _RefuseUnavailable(leftAvailable, "Intra_16x16_Horizontal", "to the left");
        for (var y = 0; y < 16; ++y)
          for (var x = 0; x < 16; ++x)
            pred[(y << 4) + x] = left[y];

        return;

      case DC_16X16: {
        var topSum = 0;
        var leftSum = 0;
        for (var i = 0; i < 16; ++i) {
          topSum += top[i];
          leftSum += left[i];
        }

        var value = topAvailable && leftAvailable ? (topSum + leftSum + 16) >> 5
          : topAvailable ? (topSum + 8) >> 4
          : leftAvailable ? (leftSum + 8) >> 4
          : 128;

        pred[..256].Fill((byte)value);
        return;
      }

      case PLANE_16X16: {
        _RefuseUnavailable(topAvailable && leftAvailable && topLeftAvailable, "Intra_16x16_Plane",
          "above, to the left and above-left");

        // A plane through the neighbours: 'a' is its height at the far corner, 'b' and 'c' its two
        // gradients, each a weighted difference across the eight samples either side of the middle
        // (equations 8-107 to 8-111). p[−1,−1] is the sample the two arms meet at, which is why the
        // reads at offset −1 fall back to it.
        var horizontal = 0;
        var vertical = 0;
        for (var i = 0; i < 8; ++i) {
          horizontal += (i + 1) * (top[8 + i] - _Top(top, topLeft, 6 - i));
          vertical += (i + 1) * (left[8 + i] - _Left(left, topLeft, 6 - i));
        }

        var a = 16 * (left[15] + top[15]);
        var b = (5 * horizontal + 32) >> 6;
        var c = (5 * vertical + 32) >> 6;

        for (var y = 0; y < 16; ++y)
          for (var x = 0; x < 16; ++x)
            pred[(y << 4) + x] = _Clip((a + b * (x - 7) + c * (y - 7) + 16) >> 5);

        return;
      }

      default:
        throw new InvalidDataException(
          $"An H.264 macroblock states Intra16x16PredMode {mode}. H.264, Table 8-3 defines 0 to 3 only.");
    }
  }

  /// <summary>
  /// Predicts both 8x8 chroma blocks of a macroblock for 4:2:0 — clause 8.3.4.
  /// </summary>
  /// <remarks>
  /// The DC mode is the one that is not simply the luma mode at half the size. Each of the four 4x4
  /// quadrants takes its own average, and which neighbours a quadrant averages depends on where it is:
  /// the two on the diagonal use whatever is available on both sides, the top-right quadrant prefers
  /// the row above it and the bottom-left the column beside it (clause 8.3.4.1). A chroma block
  /// predicted with one average over all eight samples is a picture whose colour is right on average
  /// and wrong at every edge.
  /// </remarks>
  internal static void PredictChroma8x8(
    int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft,
    bool topAvailable, bool leftAvailable, bool topLeftAvailable, Span<byte> pred) {
    switch (mode) {
      case DC_CHROMA:
        for (var quadrant = 0; quadrant < 4; ++quadrant) {
          var xO = (quadrant & 1) << 2;
          var yO = (quadrant >> 1) << 2;

          var topSum = 0;
          var leftSum = 0;
          for (var i = 0; i < 4; ++i) {
            topSum += top[xO + i];
            leftSum += left[yO + i];
          }

          var bothFirst = xO == yO;
          var preferTop = xO == 4 && yO == 0;

          var value = bothFirst
            ? topAvailable && leftAvailable ? (topSum + leftSum + 4) >> 3
              : topAvailable ? (topSum + 2) >> 2
              : leftAvailable ? (leftSum + 2) >> 2
              : 128
            : preferTop
              ? topAvailable ? (topSum + 2) >> 2
                : leftAvailable ? (leftSum + 2) >> 2
                : 128
              : leftAvailable ? (leftSum + 2) >> 2
                : topAvailable ? (topSum + 2) >> 2
                : 128;

          for (var y = 0; y < 4; ++y)
            for (var x = 0; x < 4; ++x)
              pred[((yO + y) << 3) + xO + x] = (byte)value;
        }

        return;

      case HORIZONTAL_CHROMA:
        _RefuseUnavailable(leftAvailable, "Intra_Chroma_Horizontal", "to the left");
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x)
            pred[(y << 3) + x] = left[y];

        return;

      case VERTICAL_CHROMA:
        _RefuseUnavailable(topAvailable, "Intra_Chroma_Vertical", "above");
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x)
            pred[(y << 3) + x] = top[x];

        return;

      case PLANE_CHROMA: {
        _RefuseUnavailable(topAvailable && leftAvailable && topLeftAvailable, "Intra_Chroma_Plane",
          "above, to the left and above-left");

        var horizontal = 0;
        var vertical = 0;
        for (var i = 0; i < 4; ++i) {
          horizontal += (i + 1) * (top[4 + i] - _Top(top, topLeft, 2 - i));
          vertical += (i + 1) * (left[4 + i] - _Left(left, topLeft, 2 - i));
        }

        // 34 rather than the luma 5, because the arms are half as long: equations 8-119 and 8-120
        // with ChromaArrayType equal to 1.
        var a = 16 * (left[7] + top[7]);
        var b = (34 * horizontal + 32) >> 6;
        var c = (34 * vertical + 32) >> 6;

        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x)
            pred[(y << 3) + x] = _Clip((a + b * (x - 3) + c * (y - 3) + 16) >> 5);

        return;
      }

      default:
        throw new InvalidDataException(
          $"An H.264 macroblock states intra_chroma_pred_mode {mode}. H.264, Table 8-5 defines 0 to 3 only.");
    }
  }

  /// <summary>Reads <c>p[x,−1]</c>, where index −1 is the corner sample <c>p[−1,−1]</c>.</summary>
  private static int _Top(ReadOnlySpan<byte> top, byte topLeft, int x) => x < 0 ? topLeft : top[x];

  /// <summary>Reads <c>p[−1,y]</c>, where index −1 is likewise the corner.</summary>
  private static int _Left(ReadOnlySpan<byte> left, byte topLeft, int y) => y < 0 ? topLeft : left[y];

  private static byte _Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  /// <summary>
  /// Refuses a prediction mode that needs samples this block does not have.
  /// </summary>
  /// <remarks>
  /// Clause 8.3.1.2 makes it a requirement of bitstream conformance that a mode is only chosen where
  /// its neighbours exist, so reaching this is a stream that is malformed or a slice being decoded
  /// from the wrong bit position. Predicting from a substitute anyway would produce a picture with no
  /// relation to what was encoded, and one that looks like a picture.
  /// </remarks>
  private static void _RefuseUnavailable(bool available, string mode, string neighbours) {
    if (available)
      return;

    throw new InvalidDataException(
      $"An H.264 macroblock selects {mode} where the samples {neighbours} of it are not available — the block is at "
      + "a picture or slice edge, or its neighbour is inter-coded in a stream with constrained_intra_pred_flag set. "
      + "H.264, clause 8.3.1.2 does not allow that mode to be chosen there.");
  }
}
