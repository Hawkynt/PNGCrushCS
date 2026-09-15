using System;
using System.IO;
using FileFormat.Codecs;
using FileFormat.Core;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Writes Theora I intra pictures using the stream's fixed setup header.
/// </summary>
/// <remarks>
/// This first encoder deliberately chooses the smallest coherent subset of the encoder problem rather
/// than pretending to have rate control: every picture is a key frame, every block carries only its
/// direct-current coefficient, and the setup header uses one constant quantiser. The result is blocky
/// by design but it is ordinary Theora syntax — including raster-order DC prediction and the token
/// layer's coded-order coefficient pass — and can be decoded independently of this implementation.
/// </remarks>
internal sealed class TheoraEncoder {

  private const int _QUANTISATION_INDEX = 0;
  private const int _DC_MATRIX = TheoraEncoderHeaders.QuantiserMatrixValue * 4;

  private readonly TheoraIdentificationHeader _header;
  private readonly TheoraGeometry _geometry;
  private readonly int _pictureWidth;
  private readonly int _pictureHeight;

  internal TheoraEncoder(TheoraIdentificationHeader header) {
    this._header = header;
    this._geometry = new(header);
    this._pictureWidth = header.PictureWidth;
    this._pictureHeight = header.PictureHeight;
  }

  internal byte[] Encode(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._pictureWidth || frame.Height != this._pictureHeight)
      throw new InvalidDataException(
        $"This Theora stream is {this._pictureWidth}x{this._pictureHeight}; a {frame.Width}x{frame.Height} picture arrived.");

    var picture = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv444P8, this._pictureWidth, this._pictureHeight);
    var planes = this._PadAndFlip(picture);
    var dc = this._DcCoefficients(planes);
    var residual = this._PredictDcResiduals(dc);

    var bits = new TheoraBitWriter();

    // Data packet, intra frame, one quantisation index, no additional index, three reserved zero bits.
    bits.WriteBit(0);
    bits.WriteBit(0);
    bits.WriteBits(6, _QUANTISATION_INDEX);
    bits.WriteBit(0);
    bits.WriteBits(3, 0);

    // DC coefficient pass: table zero for luma and chroma. All eighty setup-header tables are the
    // same balanced tree, so a token is its own five-bit Huffman code.
    bits.WriteBits(4, 0);
    bits.WriteBits(4, 0);
    for (var block = 0; block < residual.Length; ++block)
      _WriteCoefficient(bits, residual[block]);

    // The first AC pass always reads both codebook selectors. One EOB token with a zero run then
    // means every unfinished coded block, which is every block in this all-intra frame.
    bits.WriteBits(4, 0);
    bits.WriteBits(4, 0);
    bits.WriteBits(5, 6);
    bits.WriteBits(12, 0);

    return bits.Finish();
  }

  /// <summary>
  /// Converts the top-down picture planes to Theora's bottom-up coded frame and replicates the top
  /// and right edges into the macroblock padding outside the displayed picture.
  /// </summary>
  private byte[] _PadAndFlip(byte[] picture) {
    var frameWidth = this._header.FrameWidth;
    var frameHeight = this._header.FrameHeight;
    var frameSamples = checked(frameWidth * frameHeight);
    var pictureSamples = checked(this._pictureWidth * this._pictureHeight);
    var result = new byte[checked(frameSamples * 3)];

    for (var plane = 0; plane < 3; ++plane) {
      var sourceOffset = plane * pictureSamples;
      var targetOffset = plane * frameSamples;

      for (var y = 0; y < frameHeight; ++y) {
        var sourceY = Math.Clamp(this._pictureHeight - 1 - y, 0, this._pictureHeight - 1);
        var sourceRow = sourceOffset + sourceY * this._pictureWidth;
        var targetRow = targetOffset + y * frameWidth;

        for (var x = 0; x < frameWidth; ++x)
          result[targetRow + x] = picture[sourceRow + Math.Min(x, this._pictureWidth - 1)];
      }
    }

    return result;
  }

  /// <summary>Chooses the quantised DC value whose normative DC-only reconstruction best matches each block mean.</summary>
  private int[] _DcCoefficients(byte[] planes) {
    var geometry = this._geometry;
    var frameSamples = checked(this._header.FrameWidth * this._header.FrameHeight);
    var result = new int[geometry.BlockCount];

    for (var block = 0; block < geometry.BlockCount; ++block) {
      var plane = geometry.BlockPlane[block];
      var x = geometry.BlockColumn[block] * TheoraGeometry.BLOCK_SIZE;
      var y = geometry.BlockRow[block] * TheoraGeometry.BLOCK_SIZE;
      var planeOffset = plane * frameSamples;
      var sum = 0;

      for (var row = 0; row < TheoraGeometry.BLOCK_SIZE; ++row) {
        var at = planeOffset + (y + row) * this._header.FrameWidth + x;
        for (var column = 0; column < TheoraGeometry.BLOCK_SIZE; ++column)
          sum += planes[at + column];
      }

      var mean = (sum + 32) >> 6;
      result[block] = _NearestDc(mean);
    }

    return result;
  }

  /// <summary>
  /// Inverts section 7.8's DC predictor. The decoder adds this residual to a predictor built from
  /// already reconstructed neighbouring DC values; the encoder has those target values already, so
  /// it computes the same predictor and subtracts it.
  /// </summary>
  private int[] _PredictDcResiduals(int[] dc) {
    var geometry = this._geometry;
    var residual = new int[geometry.BlockCount];
    Span<int> predictors = stackalloc int[4];

    for (var plane = 0; plane < 3; ++plane) {
      var blocksWide = geometry.PlaneBlocksWide[plane];
      var blocksHigh = geometry.PlaneBlocksHigh[plane];

      for (var row = 0; row < blocksHigh; ++row)
      for (var column = 0; column < blocksWide; ++column) {
        var block = geometry.BlockAt(plane, column, row);
        var available = 0;
        predictors.Clear();

        if (column > 0) {
          available |= 1;
          predictors[0] = geometry.BlockAt(plane, column - 1, row);
        }
        if (column > 0 && row > 0) {
          available |= 2;
          predictors[1] = geometry.BlockAt(plane, column - 1, row - 1);
        }
        if (row > 0) {
          available |= 4;
          predictors[2] = geometry.BlockAt(plane, column, row - 1);
        }
        if (column < blocksWide - 1 && row > 0) {
          available |= 8;
          predictors[3] = geometry.BlockAt(plane, column + 1, row - 1);
        }

        var prediction = 0;
        if (available != 0) {
          var weights = TheoraTables.DcPredictorWeights[available];
          for (var neighbour = 0; neighbour < 4; ++neighbour)
            if ((available & (1 << neighbour)) != 0)
              prediction += weights[neighbour] * dc[predictors[neighbour]];
          prediction /= weights[4];

          if ((available & 0b0111) == 0b0111) {
            var below = dc[predictors[2]];
            var left = dc[predictors[0]];
            var belowLeft = dc[predictors[1]];
            if (Math.Abs(prediction - below) > 128)
              prediction = below;
            else if (Math.Abs(prediction - left) > 128)
              prediction = left;
            else if (Math.Abs(prediction - belowLeft) > 128)
              prediction = belowLeft;
          }
        }

        residual[block] = (short)(dc[block] - prediction);
      }
    }

    return residual;
  }

  private static int _NearestDc(int sample) {
    var best = 0;
    var bestError = int.MaxValue;

    for (var dc = -128; dc <= 127; ++dc) {
      var reconstructed = Math.Clamp(128 + ((dc * _DC_MATRIX + 15) >> 5), 0, 255);
      var error = Math.Abs(reconstructed - sample);
      if (error >= bestError)
        continue;

      best = dc;
      bestError = error;
      if (error == 0)
        break;
    }

    return best;
  }

  /// <summary>Writes one coefficient token from Table 7.38 using the fixed five-bit Huffman tree.</summary>
  private static void _WriteCoefficient(TheoraBitWriter bits, int value) {
    var magnitude = Math.Abs(value);
    switch (magnitude) {
      case 0:
        bits.WriteBits(5, 7);
        bits.WriteBits(3, 0); // one zero
        return;
      case 1:
        bits.WriteBits(5, value > 0 ? 9u : 10u);
        return;
      case 2:
        bits.WriteBits(5, value > 0 ? 11u : 12u);
        return;
      case <= 6:
        bits.WriteBits(5, (uint)(magnitude + 10));
        bits.WriteBit(value < 0 ? 1 : 0);
        return;
      case <= 8:
        _Magnitude(bits, 17, value, 7, 1);
        return;
      case <= 12:
        _Magnitude(bits, 18, value, 9, 2);
        return;
      case <= 20:
        _Magnitude(bits, 19, value, 13, 3);
        return;
      case <= 36:
        _Magnitude(bits, 20, value, 21, 4);
        return;
      case <= 68:
        _Magnitude(bits, 21, value, 37, 5);
        return;
      case <= 580:
        _Magnitude(bits, 22, value, 69, 9);
        return;
      default:
        throw new InvalidDataException($"Theora DC prediction produced residual {value}, beyond token 22's ±580 range.");
    }
  }

  private static void _Magnitude(TheoraBitWriter bits, uint token, int value, int first, int extraBits) {
    bits.WriteBits(5, token);
    bits.WriteBit(value < 0 ? 1 : 0);
    bits.WriteBits(extraBits, (uint)(Math.Abs(value) - first));
  }
}
