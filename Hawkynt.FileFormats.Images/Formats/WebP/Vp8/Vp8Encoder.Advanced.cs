using System;
using System.IO;
using System.Threading.Tasks;
using FileFormat.Core;

namespace FileFormat.WebP.Vp8;

/// <summary>Advanced VP8 token-partition emission.</summary>
internal sealed partial class Vp8Encoder {

  internal static byte[] Encode(RawImage image, int quality, int tokenPartitions, bool threadTokenPartitions) {
    ArgumentNullException.ThrowIfNull(image);
    if (quality is < 0 or > 100)
      throw new ArgumentOutOfRangeException(nameof(quality));
    if (tokenPartitions is not (1 or 2 or 4 or 8))
      throw new ArgumentOutOfRangeException(nameof(tokenPartitions), "VP8 supports exactly 1, 2, 4, or 8 coefficient token partitions.");
    if (image.Width is < 1 or > 16383 || image.Height is < 1 or > 16383)
      throw new InvalidDataException("VP8 keyframe dimensions must be 1..16383.");

    var encoder = new Vp8Encoder(image.Width, image.Height, quality);
    encoder._LoadSource(image);
    encoder._EncodeAllMacroblocks();
    return encoder._WriteBitstream(tokenPartitions, threadTokenPartitions);
  }

  /// <summary>
  /// Writes partition 0 and independent coefficient arithmetic streams. Prediction/mode coding
  /// stays serial; coefficient rows can be emitted concurrently after row-start contexts are known.
  /// </summary>
  private byte[] _WriteBitstream(int tokenPartitions, bool threadTokenPartitions) {
    var header = new Vp8BitWriter(_mbw * _mbh * 4);

    header.PutBitUniform(0); // colorspace
    header.PutBitUniform(0); // clamp type
    header.PutBitUniform(0); // segmentation disabled

    var filterLevel = _baseQ < 40 ? 0 : Math.Min(63, _baseQ * 5 >> 5);
    header.PutBitUniform(0); // normal filter
    header.PutBits((uint)filterLevel, 6);
    header.PutBits(0, 3); // sharpness
    header.PutBitUniform(0); // mode/ref LF deltas disabled

    var partitionBits = tokenPartitions switch {
      1 => 0,
      2 => 1,
      4 => 2,
      8 => 3,
      _ => throw new InvalidOperationException("Invalid VP8 token partition count reached bitstream writer."),
    };
    header.PutBits((uint)partitionBits, 2);

    header.PutBits((uint)_baseQ, 7);
    header.PutSignedBits(0, 4); // y1_dc_delta_q
    header.PutSignedBits(0, 4); // y2_dc_delta_q
    header.PutSignedBits(0, 4); // y2_ac_delta_q
    header.PutSignedBits(0, 4); // uv_dc_delta_q
    header.PutSignedBits(0, 4); // uv_ac_delta_q
    header.PutBitUniform(0); // refresh_entropy_probs

    var totalMacroblocks = _mbw * _mbh;
    var skippedMacroblocks = 0;
    for (var i = 0; i < totalMacroblocks; ++i)
      if (_mbSkip[i])
        ++skippedMacroblocks;

    var skipProbability = totalMacroblocks == 0 ? 255 : (totalMacroblocks - skippedMacroblocks) * 255 / totalMacroblocks;
    var useSkipProbability = skipProbability < 250;
    if (!useSkipProbability)
      Array.Fill(_mbSkip, false);

    // Statistics must describe the coefficients that are actually emitted. In particular, when
    // skip coding is disabled, formerly skippable macroblocks contribute their all-zero streams.
    _CountAllCoeffs();
    var adaptiveProbabilities = _EmitProbUpdatesAndComputeProbs(header);

    header.PutBitUniform(useSkipProbability ? 1 : 0);
    if (useSkipProbability)
      header.PutBits((uint)skipProbability, 8);

    _WritePredictionModes(header, useSkipProbability, skipProbability);

    var tokenPayloads = _WriteTokenPartitions(adaptiveProbabilities, tokenPartitions, threadTokenPartitions);
    return _AssembleVp8Chunk(header.Finish(), tokenPayloads);
  }

  private void _WritePredictionModes(Vp8BitWriter writer, bool useSkipProbability, int skipProbability) {
    var topI4Modes = new byte[_mbw * 4];

    for (var mby = 0; mby < _mbh; ++mby) {
      var leftI4Modes = new byte[4];
      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var macroblock = mby * _mbw + mbx;
        if (useSkipProbability)
          writer.PutBit(_mbSkip[macroblock] ? 1 : 0, (byte)skipProbability);

        if (_mbType[macroblock] == 1) {
          writer.PutBit(1, 145);
          _WriteI16Mode(writer, _mbY16Mode[macroblock]);
          var equivalentI4Mode = _mbY16Mode[macroblock] switch {
            Vp8EncPredict.DC_PRED => Vp8EncPredict.B_DC_PRED,
            Vp8EncPredict.TM_PRED => Vp8EncPredict.B_TM_PRED,
            Vp8EncPredict.V_PRED => Vp8EncPredict.B_VE_PRED,
            _ => Vp8EncPredict.B_HE_PRED,
          };
          for (var i = 0; i < 4; ++i) {
            topI4Modes[mbx * 4 + i] = equivalentI4Mode;
            leftI4Modes[i] = equivalentI4Mode;
          }
        } else {
          writer.PutBit(0, 145);
          for (var by = 0; by < 4; ++by) {
            var left = leftI4Modes[by];
            for (var bx = 0; bx < 4; ++bx) {
              var above = topI4Modes[mbx * 4 + bx];
              var mode = _mbI4Modes[macroblock * 16 + by * 4 + bx];
              _WriteI4Mode(writer, mode, above, left);
              topI4Modes[mbx * 4 + bx] = mode;
              left = mode;
            }
            leftI4Modes[by] = left;
          }
        }

        _WriteUvMode(writer, _mbUvMode[macroblock]);
      }
    }
  }

  private sealed record TokenRowContext(byte[] TopY, byte[] TopUv, byte[] TopY2);

  /// <summary>
  /// Captures coefficient non-zero neighbor state at each row boundary. VP8 selects an arithmetic
  /// partition by macroblock-row number, but probability contexts still depend on the previous row.
  /// This pre-pass therefore separates context dependency from arithmetic-coder dependency.
  /// </summary>
  private TokenRowContext[] _BuildTokenRowContexts() {
    var result = new TokenRowContext[_mbh];
    var topY = new byte[_mbw * 4];
    var topUv = new byte[_mbw * 4];
    var topY2 = new byte[_mbw];

    for (var mby = 0; mby < _mbh; ++mby) {
      result[mby] = new((byte[])topY.Clone(), (byte[])topUv.Clone(), (byte[])topY2.Clone());
      _AdvanceTokenContextsForRow(mby, topY, topUv, topY2);
    }

    return result;
  }

  private void _AdvanceTokenContextsForRow(int mby, byte[] topY, byte[] topUv, byte[] topY2) {
    byte leftY0 = 0, leftY1 = 0, leftY2 = 0, leftY3 = 0;
    byte leftU0 = 0, leftU1 = 0, leftV0 = 0, leftV1 = 0, leftY2Dc = 0;

    for (var mbx = 0; mbx < _mbw; ++mbx) {
      var macroblock = mby * _mbw + mbx;
      var isI16 = _mbType[macroblock] == 1;

      if (_mbSkip[macroblock]) {
        if (isI16) {
          topY2[mbx] = 0;
          leftY2Dc = 0;
        }
        Array.Clear(topY, mbx * 4, 4);
        Array.Clear(topUv, mbx * 4, 4);
        leftY0 = leftY1 = leftY2 = leftY3 = 0;
        leftU0 = leftU1 = leftV0 = leftV1 = 0;
        continue;
      }

      if (isI16) {
        var nonZero = _HasCoefficients(_mbY2, macroblock * 16, 0);
        topY2[mbx] = nonZero;
        leftY2Dc = nonZero;
      }

      var firstY = isI16 ? 1 : 0;
      for (var by = 0; by < 4; ++by) {
        var left = by switch { 0 => leftY0, 1 => leftY1, 2 => leftY2, _ => leftY3 };
        for (var bx = 0; bx < 4; ++bx) {
          var block = by * 4 + bx;
          var nonZero = _HasCoefficients(_mbYAc, macroblock * 256 + block * 16, firstY);
          topY[mbx * 4 + bx] = nonZero;
          left = nonZero;
        }
        switch (by) {
          case 0: leftY0 = left; break;
          case 1: leftY1 = left; break;
          case 2: leftY2 = left; break;
          default: leftY3 = left; break;
        }
      }

      for (var by = 0; by < 2; ++by) {
        var left = by == 0 ? leftU0 : leftU1;
        for (var bx = 0; bx < 2; ++bx) {
          var block = by * 2 + bx;
          var nonZero = _HasCoefficients(_mbUv, macroblock * 128 + block * 16, 0);
          topUv[mbx * 4 + bx] = nonZero;
          left = nonZero;
        }
        if (by == 0) leftU0 = left;
        else leftU1 = left;
      }

      for (var by = 0; by < 2; ++by) {
        var left = by == 0 ? leftV0 : leftV1;
        for (var bx = 0; bx < 2; ++bx) {
          var block = by * 2 + bx;
          var nonZero = _HasCoefficients(_mbUv, macroblock * 128 + (4 + block) * 16, 0);
          topUv[mbx * 4 + 2 + bx] = nonZero;
          left = nonZero;
        }
        if (by == 0) leftV0 = left;
        else leftV1 = left;
      }
    }
  }

  private static byte _HasCoefficients(short[] coefficients, int offset, int firstCoefficient) {
    for (var i = firstCoefficient; i < 16; ++i)
      if (coefficients[offset + i] != 0)
        return 1;
    return 0;
  }

  private byte[][] _WriteTokenPartitions(byte[] probabilities, int tokenPartitions, bool threaded) {
    var rowContexts = _BuildTokenRowContexts();
    var result = new byte[tokenPartitions][];

    void EncodePartition(int partition) {
      var writer = new Vp8BitWriter(Math.Max(1024, _mbw * _mbh * 8 / tokenPartitions));
      for (var mby = partition; mby < _mbh; mby += tokenPartitions)
        _WriteCoefficientRow(writer, probabilities, mby, rowContexts[mby]);
      result[partition] = writer.Finish();
    }

    if (threaded && tokenPartitions > 1 && Environment.ProcessorCount > 1) {
      Parallel.For(
        0,
        tokenPartitions,
        new ParallelOptions { MaxDegreeOfParallelism = Math.Min(tokenPartitions, Environment.ProcessorCount) },
        EncodePartition);
    } else {
      for (var partition = 0; partition < tokenPartitions; ++partition)
        EncodePartition(partition);
    }

    return result;
  }

  private void _WriteCoefficientRow(Vp8BitWriter writer, byte[] probabilities, int mby, TokenRowContext rowContext) {
    var topY = (byte[])rowContext.TopY.Clone();
    var topUv = (byte[])rowContext.TopUv.Clone();
    var topY2 = (byte[])rowContext.TopY2.Clone();
    byte leftY0 = 0, leftY1 = 0, leftY2 = 0, leftY3 = 0;
    byte leftU0 = 0, leftU1 = 0, leftV0 = 0, leftV1 = 0, leftY2Dc = 0;

    for (var mbx = 0; mbx < _mbw; ++mbx) {
      var macroblock = mby * _mbw + mbx;
      var isI16 = _mbType[macroblock] == 1;

      if (_mbSkip[macroblock]) {
        if (isI16) {
          topY2[mbx] = 0;
          leftY2Dc = 0;
        }
        Array.Clear(topY, mbx * 4, 4);
        Array.Clear(topUv, mbx * 4, 4);
        leftY0 = leftY1 = leftY2 = leftY3 = 0;
        leftU0 = leftU1 = leftV0 = leftV1 = 0;
        continue;
      }

      if (isI16) {
        var context = (byte)(topY2[mbx] + leftY2Dc);
        var nonZero = _PutCoeffs(writer, PlaneY2, context, _mbY2, macroblock * 16, 0, probabilities);
        topY2[mbx] = nonZero;
        leftY2Dc = nonZero;
      }

      var plane = isI16 ? PlaneY1WithY2 : PlaneY1SansY2;
      var firstY = isI16 ? 1 : 0;
      for (var by = 0; by < 4; ++by) {
        var left = by switch { 0 => leftY0, 1 => leftY1, 2 => leftY2, _ => leftY3 };
        for (var bx = 0; bx < 4; ++bx) {
          var block = by * 4 + bx;
          var context = (byte)(topY[mbx * 4 + bx] + left);
          var nonZero = _PutCoeffs(writer, plane, context, _mbYAc, macroblock * 256 + block * 16, firstY, probabilities);
          topY[mbx * 4 + bx] = nonZero;
          left = nonZero;
        }
        switch (by) {
          case 0: leftY0 = left; break;
          case 1: leftY1 = left; break;
          case 2: leftY2 = left; break;
          default: leftY3 = left; break;
        }
      }

      for (var by = 0; by < 2; ++by) {
        var left = by == 0 ? leftU0 : leftU1;
        for (var bx = 0; bx < 2; ++bx) {
          var block = by * 2 + bx;
          var context = (byte)(topUv[mbx * 4 + bx] + left);
          var nonZero = _PutCoeffs(writer, PlaneUV, context, _mbUv, macroblock * 128 + block * 16, 0, probabilities);
          topUv[mbx * 4 + bx] = nonZero;
          left = nonZero;
        }
        if (by == 0) leftU0 = left;
        else leftU1 = left;
      }

      for (var by = 0; by < 2; ++by) {
        var left = by == 0 ? leftV0 : leftV1;
        for (var bx = 0; bx < 2; ++bx) {
          var block = by * 2 + bx;
          var context = (byte)(topUv[mbx * 4 + 2 + bx] + left);
          var nonZero = _PutCoeffs(writer, PlaneUV, context, _mbUv, macroblock * 128 + (4 + block) * 16, 0, probabilities);
          topUv[mbx * 4 + 2 + bx] = nonZero;
          left = nonZero;
        }
        if (by == 0) leftV0 = left;
        else leftV1 = left;
      }
    }
  }

  private byte[] _AssembleVp8Chunk(byte[] firstPartition, byte[][] tokenPartitions) {
    const int maxFirstPartitionLength = (1 << 19) - 1;
    const int maxSizedTokenPartitionLength = (1 << 24) - 1;

    if (firstPartition.Length > maxFirstPartitionLength)
      throw new InvalidDataException($"VP8 first partition is {firstPartition.Length} bytes; maximum is {maxFirstPartitionLength}.");
    for (var i = 0; i < tokenPartitions.Length - 1; ++i)
      if (tokenPartitions[i].Length > maxSizedTokenPartitionLength)
        throw new InvalidDataException($"VP8 token partition {i} is too large for its 24-bit size field.");

    var sizeTableLength = checked((tokenPartitions.Length - 1) * 3);
    var totalLength = checked(10 + firstPartition.Length + sizeTableLength);
    foreach (var partition in tokenPartitions)
      totalLength = checked(totalLength + partition.Length);

    var result = new byte[totalLength];
    var frameTag = 1u << 4 | (uint)firstPartition.Length << 5; // keyframe, version 0, show_frame
    result[0] = (byte)frameTag;
    result[1] = (byte)(frameTag >> 8);
    result[2] = (byte)(frameTag >> 16);
    result[3] = 0x9d;
    result[4] = 0x01;
    result[5] = 0x2a;
    result[6] = (byte)_width;
    result[7] = (byte)(_width >> 8 & 0x3f);
    result[8] = (byte)_height;
    result[9] = (byte)(_height >> 8 & 0x3f);

    Buffer.BlockCopy(firstPartition, 0, result, 10, firstPartition.Length);
    var tableOffset = 10 + firstPartition.Length;
    var payloadOffset = tableOffset + sizeTableLength;

    for (var i = 0; i < tokenPartitions.Length - 1; ++i) {
      var length = tokenPartitions[i].Length;
      result[tableOffset + i * 3] = (byte)length;
      result[tableOffset + i * 3 + 1] = (byte)(length >> 8);
      result[tableOffset + i * 3 + 2] = (byte)(length >> 16);
    }

    foreach (var partition in tokenPartitions) {
      Buffer.BlockCopy(partition, 0, result, payloadOffset, partition.Length);
      payloadOffset += partition.Length;
    }

    return result;
  }
}
