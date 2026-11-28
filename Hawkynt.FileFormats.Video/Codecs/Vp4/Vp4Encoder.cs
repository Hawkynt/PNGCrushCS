using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Codecs.Vp3;

namespace FileFormat.Codecs.Vp4;

/// <summary>
/// Writes On2 VP4: key frames, and inter frames whose blocks are either copied from the frame before
/// or coded as a residual against it in place.
/// </summary>
/// <remarks>
/// The package's own encoder — FFmpeg has none — writing the syntax <see cref="Vp4Decoder"/> reads.
/// An inter frame states, macroblock by macroblock, whether all, some or none of its blocks are coded,
/// with VP4's run-length and block-pattern codes; every coded macroblock uses VP4's "inter, no motion
/// vector" mode, written in mode scheme 5 where that mode costs one bit. Coefficients go out with the
/// first coefficient table of each group, each block's DC as its difference from VP4's own DC
/// prediction, and an end-of-block of one block wherever a block stops short. Every frame is decoded
/// again with <see cref="Vp4Decoder"/> to give the reference the next one is coded against, so the
/// encoder's idea of the picture is the decoder's.
/// </remarks>
internal sealed class Vp4Encoder {

  private const int _MODE_INTRA = 1;
  private const int _MODE_INTER_NO_MV = 0;

  /// <summary>The largest coefficient magnitude VP4's tokens can state.</summary>
  private const int _MAX_LEVEL = 580;

  private static readonly (int Code, int Length)[][] _Codes = _BuildCodes();

  private readonly int _width;
  private readonly int _height;
  private readonly int _quality;
  private readonly Vp4Decoder _reconstruction;
  private readonly int[] _fragmentWidth;
  private readonly int[] _fragmentHeight;
  private readonly short[,] _dequant;
  private byte[][]? _reference;

  internal Vp4Encoder(int width, int height, int quality) {
    this._reconstruction = new(width, height);
    this._width = this._reconstruction.CodedWidth;
    this._height = this._reconstruction.CodedHeight;
    if (this._width / 16 > 255 || this._height / 16 > 255)
      throw new NotSupportedException($"VP4 states its macroblock rows and columns in eight bits each; {width}x{height} needs more.");
    this._quality = quality;
    this._fragmentWidth = [this._width / 8, this._width / 16];
    this._fragmentHeight = [this._height / 8, this._height / 16];
    this._dequant = _Dequantiser(quality);
  }

  /// <summary>Codes one picture, given as three planes of the padded size, bottom row first.</summary>
  internal byte[] Encode(byte[][] source, bool keyFrame) {
    keyFrame |= this._reference == null;
    var writer = new MsMpeg4BitWriter();
    writer.Write(keyFrame ? 0 : 1, 1);
    writer.Write(0, 1);
    writer.Write(this._quality, 6);
    if (keyFrame) {
      writer.Write(0, 8); // width and height codes, which VP4 does not use
      writer.Write(3, 5); // bitstream version
      writer.Write(0, 3); // coding type and two reserved bits
      writer.Write(this._height / 16, 8);
      writer.Write(this._width / 16, 8);
      writer.Write(1, 5);
      writer.Write(1, 3);
      writer.Write(1, 5);
      writer.Write(1, 3);
      writer.Write(0, 2);
    }

    // Levels in scan order for every block, and which blocks carry anything.
    var levels = new short[3][][];
    var coded = new bool[3][];
    for (var plane = 0; plane < 3; ++plane) {
      var p = plane == 0 ? 0 : 1;
      var count = this._fragmentWidth[p] * this._fragmentHeight[p];
      levels[plane] = new short[count][];
      coded[plane] = new bool[count];
      for (var f = 0; f < count; ++f) {
        var x = f % this._fragmentWidth[p];
        var y = f / this._fragmentWidth[p];
        var block = levels[plane][f] = this._Quantise(source[plane], this._reference?[plane], plane, x, y, keyFrame);
        coded[plane][f] = keyFrame || _AnyNonZero(block);
      }
    }

    if (!keyFrame) {
      this._WriteMacroblocks(writer, coded);
      writer.Write(5, 3); // mode scheme 5, where "inter, no motion vector" is the one-bit code
      this._WriteModes(writer, coded[0]);
    }

    this._WriteCoefficients(writer, levels, coded, keyFrame);

    var bytes = writer.ToArray();
    this._reference = this._reconstruction.Decode(bytes);
    return bytes;
  }

  private static bool _AnyNonZero(short[] block) {
    foreach (var value in block)
      if (value != 0)
        return true;
    return false;
  }

  // ============================================================================================
  // Transform and quantisation
  // ============================================================================================

  private static short[,] _Dequantiser(int quality) {
    var result = new short[2, 128];
    for (var inter = 0; inter < 2; ++inter)
      for (var plane = 0; plane < 2; ++plane) {
        int dcScale = plane == 0 ? Vp4Tables.LumaDcScale[quality] : Vp4Tables.ChromaDcScale[quality];
        for (var i = 0; i < 64; ++i) {
          int coefficient = Vp4Tables.GenericDequant[i];
          var minimum = 8 << (inter + (i == 0 ? 1 : 0));
          var scale = i != 0 ? Vp4Tables.AcScale[quality] : dcScale;
          var bias = (1 + inter) * 3;
          result[inter, plane * 64 + i] = (short)(i == 0
            ? Math.Clamp(scale * coefficient / 100 * 4, minimum, 4096)
            : (scale * (coefficient - bias) / 100 + bias) * 4);
        }
      }

    return result;
  }

  /// <summary>One block's levels in zigzag order, intra against mid-grey or inter against the reference in place.</summary>
  private short[] _Quantise(byte[] source, byte[]? reference, int plane, int x, int y, bool intra) {
    var p = plane == 0 ? 0 : 1;
    var stride = this._fragmentWidth[p] * 8;
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    for (var r = 0; r < 8; ++r)
      for (var c = 0; c < 8; ++c) {
        var at = (8 * y + r) * stride + 8 * x + c;
        samples[r * 8 + c] = source[at] - (intra ? 128 : reference![at]);
      }

    Vp3ForwardDct.Transform(samples, coefficients);
    var levels = new short[64];
    var inter = intra ? 0 : 1;
    for (var i = 0; i < 64; ++i) {
      var natural = Mpeg4Quantisation.ZigZag[i];
      var step = this._dequant[inter, p * 64 + natural];
      var level = (int)Math.Round(coefficients[natural] / step, MidpointRounding.AwayFromZero);
      levels[i] = (short)Math.Clamp(level, -_MAX_LEVEL, _MAX_LEVEL);
    }

    return levels;
  }

  // ============================================================================================
  // Macroblock coding and modes
  // ============================================================================================

  private void _WriteMacroblocks(MsMpeg4BitWriter writer, bool[][] coded) {
    // Every macroblock of the three planes in the order the decoder visits them, with its pattern.
    var states = new System.Collections.Generic.List<(int State, int Pattern)>();
    for (var plane = 0; plane < 3; ++plane) {
      var p = plane == 0 ? 0 : 1;
      var fw = this._fragmentWidth[p];
      var fh = this._fragmentHeight[p];
      var mbWidth = (fw * 8 + 15) / 16;
      var mbHeight = (fh * 8 + 15) / 16;
      for (var sbY = 0; sbY < (fh * 8 + 31) / 32; ++sbY)
        for (var sbX = 0; sbX < (fw * 8 + 31) / 32; ++sbX)
          for (var j = 0; j < 4; ++j) {
            var mbX = 2 * sbX + (j >> 1);
            var mbY = (2 * sbY + (j >> 1)) ^ (j & 1);
            if (mbX >= mbWidth || mbY >= mbHeight)
              continue;

            int pattern = 0, inside = 0;
            for (var k = 0; k < 4; ++k) {
              var x = 2 * mbX + (k & 1);
              var y = 2 * mbY + (k >> 1);
              if (x >= fw || y >= fh)
                continue;
              inside |= 8 >> k;
              if (coded[plane][y * fw + x])
                pattern |= 8 >> k;
            }

            states.Add(pattern == inside ? (2, 0xF) : pattern == 0 ? (0, 0) : (1, pattern));
          }
    }

    // The fully coded macroblocks as runs, then among the rest the partly coded ones as runs.
    _WriteRuns(writer, states.ConvertAll(static s => s.State == 2 ? 1 : 0));
    var rest = states.FindAll(static s => s.State != 2).ConvertAll(static s => s.State == 1 ? 1 : 0);
    if (rest.Count != 0)
      _WriteRuns(writer, rest);

    var table = 0;
    foreach (var (state, pattern) in states) {
      if (state != 1)
        continue;
      var v = pattern - 1;
      writer.Write(Vp4Tables.BlockPatternVlc[(table * 14 + v) * 2], Vp4Tables.BlockPatternVlc[(table * 14 + v) * 2 + 1]);
      table = Vp4Tables.BlockPatternSelector[v];
    }
  }

  private static void _WriteRuns(MsMpeg4BitWriter writer, System.Collections.Generic.List<int> bits) {
    writer.Write(bits[0], 1);
    for (var i = 0; i < bits.Count;) {
      var run = 1;
      while (i + run < bits.Count && bits[i + run] == bits[i])
        ++run;
      _WriteRun(writer, run);
      i += run;
    }
  }

  /// <summary>The inverse of <c>vp4_get_mb_count</c>.</summary>
  private static void _WriteRun(MsMpeg4BitWriter writer, int run) {
    var remainder = run - 1;
    while (remainder >= 256) {
      writer.Write(0x1FF, 9);
      remainder -= 256;
    }

    if (remainder == 0) {
      writer.Write(0, 1);
      return;
    }

    if (remainder == 1) {
      writer.Write(2, 2);
      return;
    }

    var n = 31 - int.LeadingZeroCount(remainder);
    // n + 1 ones and a nought, then n bits; at n = 7 the prefix is eight ones and a nought.
    writer.Write(((1 << (n + 1)) - 1) << 1, n + 2);
    writer.Write(remainder - (1 << n), n);
  }

  private void _WriteModes(MsMpeg4BitWriter writer, bool[] luma) {
    var fw = this._fragmentWidth[0];
    var mbWidth = this._width / 16;
    var mbHeight = this._height / 16;
    for (var sbY = 0; sbY < (this._height + 31) / 32; ++sbY)
      for (var sbX = 0; sbX < (this._width + 31) / 32; ++sbX)
        for (var j = 0; j < 4; ++j) {
          var mbX = 2 * sbX + (j >> 1);
          var mbY = 2 * sbY + (((j >> 1) + j) & 1);
          if (mbX >= mbWidth || mbY >= mbHeight)
            continue;
          var any = false;
          for (var k = 0; k < 4; ++k)
            any |= luma[(2 * mbY + (k >> 1)) * fw + 2 * mbX + (k & 1)];
          if (any)
            writer.Write(0, 1); // scheme 5's first code: inter, no motion vector
        }
  }

  // ============================================================================================
  // Coefficients
  // ============================================================================================

  private void _WriteCoefficients(MsMpeg4BitWriter writer, short[][][] levels, bool[][] coded, bool keyFrame) {
    writer.Write(0, 16); // the first DC and AC tables for luminance and chrominance
    var predictor = new Vp4DcPredictor((this._width + 31) / 32);
    predictor.BeginPicture();
    var type = Vp4Decoder.PredictionType[keyFrame ? _MODE_INTRA : _MODE_INTER_NO_MV];

    for (var plane = 0; plane < 3; ++plane) {
      var p = plane == 0 ? 0 : 1;
      var fw = this._fragmentWidth[p];
      var fh = this._fragmentHeight[p];
      predictor.BeginPlane(fw);
      for (var sbY = 0; sbY * 4 < fh; ++sbY)
        for (var sbX = 0; sbX * 4 < fw; ++sbX) {
          predictor.BeginSuperblock(sbX);
          for (var j = 0; j < 16; ++j) {
            int hx = Vp4Tables.HilbertOffset[2 * j];
            int hy = Vp4Tables.HilbertOffset[2 * j + 1];
            var x = 4 * sbX + hx;
            var y = 4 * sbY + hy;
            if (x >= fw || y >= fh || !coded[plane][y * fw + x])
              continue;

            var block = levels[plane][y * fw + x];
            int dc = block[0];
            var predicted = predictor.Predict(hx, hy, type);
            var difference = dc - predicted;
            if (Math.Abs(difference) > _MAX_LEVEL) {
              // The DC that goes out is the one the decoder will hold, so the predictor follows it.
              difference = Math.Sign(difference) * _MAX_LEVEL;
              dc = predicted + difference;
            }

            this._WriteBlock(writer, block, difference);
            predictor.Record(hx, hy, type, unchecked((short)dc));
          }

          predictor.EndSuperblock(sbX);
        }
    }
  }

  /// <summary>One block: its DC difference and its AC levels, as tokens, then an end of block where it stops short.</summary>
  private void _WriteBlock(MsMpeg4BitWriter writer, short[] block, int dcDifference) {
    var last = 0;
    for (var i = 63; i > 0; --i)
      if (block[i] != 0) {
        last = i;
        break;
      }

    var index = 0;
    var run = 0;
    for (var i = 0; i <= last; ++i) {
      int value = i == 0 ? dcDifference : block[i];
      if (value == 0) {
        ++run;
        continue;
      }

      this._WriteValue(writer, index, run, value);
      index = i + 1;
      run = 0;
    }

    if (index < 64)
      _Write(writer, index, 0, 0, 0, 0, 0); // an end-of-block run of one
  }

  private void _WriteValue(MsMpeg4BitWriter writer, int index, int run, int value) {
    var magnitude = Math.Abs(value);
    var negative = value < 0 ? 1 : 0;

    if (run > 0 && magnitude == 1 && run <= 17) {
      if (run <= 5) {
        _Write(writer, index, 22 + run, negative, 1, 0, 0);
      } else if (run <= 9) {
        _Write(writer, index, 28, negative, 1, run - 6, 2);
      } else {
        _Write(writer, index, 29, negative, 1, run - 10, 3);
      }

      return;
    }

    if (run > 0 && magnitude <= 3 && magnitude >= 2 && run <= 3) {
      var extra = (magnitude - 2) | (negative << 1);
      if (run == 1)
        _Write(writer, index, 30, extra, 2, 0, 0);
      else
        _Write(writer, index, 31, extra, 2, run - 2, 1);
      return;
    }

    if (run > 0) {
      if (run <= 8)
        _Write(writer, index, 7, 0, 0, run - 1, 3);
      else
        _Write(writer, index, 8, 0, 0, run - 1, 6);
      index += run;
    }

    switch (magnitude) {
      case 1:
        _Write(writer, index, negative != 0 ? 10 : 9, 0, 0, 0, 0);
        return;
      case 2:
        _Write(writer, index, negative != 0 ? 12 : 11, 0, 0, 0, 0);
        return;
      case <= 6:
        _Write(writer, index, 10 + magnitude, negative, 1, 0, 0);
        return;
      case <= 8:
        _Write(writer, index, 17, (magnitude - 7) | (negative << 1), 2, 0, 0);
        return;
      case <= 12:
        _Write(writer, index, 18, (magnitude - 9) | (negative << 2), 3, 0, 0);
        return;
      case <= 20:
        _Write(writer, index, 19, (magnitude - 13) | (negative << 3), 4, 0, 0);
        return;
      case <= 36:
        _Write(writer, index, 20, (magnitude - 21) | (negative << 4), 5, 0, 0);
        return;
      case <= 68:
        _Write(writer, index, 21, (magnitude - 37) | (negative << 5), 6, 0, 0);
        return;
      default:
        _Write(writer, index, 22, (magnitude - 69) | (negative << 9), 10, 0, 0);
        return;
    }
  }

  /// <summary>A token out of the table for coefficient <paramref name="index"/>, then its coefficient bits and its run bits.</summary>
  private static void _Write(MsMpeg4BitWriter writer, int index, int token, int coefficientBits, int coefficientLength, int runBits, int runLength) {
    var group = index == 0 ? 0 : index <= 5 ? 1 : index <= 14 ? 2 : index <= 27 ? 3 : 4;
    var (code, length) = _Codes[group * 16][token];
    if (length == 0)
      throw new InvalidOperationException($"VP4's first table of group {group} has no code for token {token}.");
    writer.Write(code, length);
    if (coefficientLength != 0)
      writer.Write(coefficientBits, coefficientLength);
    if (runLength != 0)
      writer.Write(runBits, runLength);
  }

  /// <summary>Every coefficient table's codewords, assigned in table order as <c>ff_vlc_init_from_lengths</c> assigns them.</summary>
  private static (int Code, int Length)[][] _BuildCodes() {
    var result = new (int, int)[80][];
    for (var t = 0; t < 80; ++t) {
      result[t] = new (int, int)[32];
      var consumed = 0L;
      for (var i = 0; i < 32; ++i) {
        int token = Vp4Tables.Bias[(t * 32 + i) * 2];
        int length = Vp4Tables.Bias[(t * 32 + i) * 2 + 1];
        result[t][token] = ((int)(consumed >> (32 - length)), length);
        consumed += 1L << (32 - length);
      }
    }

    return result;
  }
}
