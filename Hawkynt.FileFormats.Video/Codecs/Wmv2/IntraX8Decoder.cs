using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Wmv2;

/// <summary>
/// Decodes an IntraX8 picture — what WMV2 calls a J-frame: an intra picture coded 8x8 block by 8x8
/// block, each predicted from the reconstructed samples beside and above it in one of twelve
/// directions, with its own Huffman tables and its own deblocking filter.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/intrax8.c</c> and <c>intrax8dsp.c</c> (LGPL-2.1-or-later);
/// the tables are <see cref="IntraX8Tables"/>. Nothing of the macroblock layer around it is used: the
/// picture header hands over its quantiser and the loop-filter flag, and from then on the picture is a
/// raster of 8x8 luminance blocks, with the two chrominance blocks of a macroblock read after its
/// fourth luminance block. Every block reads the samples of blocks already finished — filtered ones,
/// because the filter runs as each block is placed — so the order here is the order of the bitstream
/// and cannot be changed.
/// </remarks>
internal sealed class IntraX8Decoder {

  // The scratch layout setup_spatial_compensation fills and the twelve predictors read: the column two
  // left of the block, the column just left of it, the corner, the row above, the row above the next
  // block, and the row two above.
  private const int _AREA1 = 0;
  private const int _AREA2 = 8;
  private const int _AREA3 = 16;
  private const int _AREA4 = 17;
  private const int _AREA5 = 25;
  private const int _AREA6 = 33;

  private static readonly MsMpeg4VlcTable[] _Orient = _BuildOrient();
  private static readonly MsMpeg4VlcTable[] _Dc = _Build(IntraX8Tables.Dc, 34, 16, "an IntraX8 DC table");
  private static readonly MsMpeg4VlcTable[] _Ac = _Build(IntraX8Tables.Ac, 77, 32, "an IntraX8 AC table");

  /// <summary><c>ac_decode_table</c>: how codes 46 to 72 carry extra bits into the run or the level.</summary>
  private static readonly uint[] _AcDecode = [
    _E(3, true, 16, 0), _E(3, true, 24, 0), _E(2, true, 4, 1), _E(3, true, 8, 1),
    _E(5, true, 32, 0), _E(4, true, 16, 1),
    _E(2, false, 0, 4), _E(2, false, 0, 8), _E(2, false, 0, 12), _E(3, false, 0, 16), _E(3, false, 0, 24),
    _E(2, false, 1, 3), _E(3, false, 1, 7),
    _E(2, true, 16, 0), _E(2, true, 20, 0), _E(2, true, 24, 0), _E(2, true, 28, 0), _E(4, true, 32, 0), _E(4, true, 48, 0),
    _E(2, true, 4, 1), _E(3, true, 8, 1), _E(4, true, 16, 1),
    _E(2, false, 0, 4), _E(3, false, 0, 8), _E(4, false, 0, 16),
    _E(2, false, 1, 3), _E(3, false, 1, 7),
  ];

  private static readonly byte[] _CrazyMixRunLevel = [
    0x22, 0x32, 0x33, 0x53, 0x23, 0x42, 0x43, 0x63,
    0x24, 0x52, 0x34, 0x73, 0x25, 0x62, 0x44, 0x83,
    0x26, 0x72, 0x35, 0x54, 0x27, 0x82, 0x45, 0x64,
    0x28, 0x92, 0x36, 0x74, 0x29, 0xa2, 0x46, 0x84,
  ];

  private static readonly byte[] _DcIndexOffset = [0, 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193];

  private static readonly byte[][] _PredictionTable = [
    [0, 8, 4, 10, 11, 2, 6, 9, 1, 3, 5, 7],
    [4, 0, 8, 11, 10, 3, 5, 2, 6, 9, 1, 7],
    [8, 0, 4, 10, 11, 1, 7, 2, 6, 9, 3, 5],
  ];

  /// <summary><c>quant_table</c>: the weights a picture with its quantisation matrix on scales each AC level by.</summary>
  private static readonly short[] _QuantTable = [
    256, 256, 256, 256, 256, 256, 259, 262,
    265, 269, 272, 275, 278, 282, 285, 288,
    292, 295, 299, 303, 306, 310, 314, 317,
    321, 325, 329, 333, 337, 341, 345, 349,
    353, 358, 362, 366, 371, 375, 379, 384,
    389, 393, 398, 403, 408, 413, 417, 422,
    428, 433, 438, 443, 448, 454, 459, 465,
    470, 476, 482, 488, 493, 499, 505, 511,
  ];

  private static readonly ushort[] _ZeroPredictionWeights = [
    640, 640, 669, 480, 708, 354, 748, 257,
    792, 198, 760, 143, 808, 101, 772, 72,
    480, 669, 537, 537, 598, 416, 661, 316,
    719, 250, 707, 185, 768, 134, 745, 97,
    354, 708, 416, 598, 488, 488, 564, 388,
    634, 317, 642, 241, 716, 179, 706, 132,
    257, 748, 316, 661, 388, 564, 469, 469,
    543, 395, 571, 311, 655, 238, 660, 180,
    198, 792, 250, 719, 317, 634, 395, 543,
    469, 469, 507, 380, 597, 299, 616, 231,
    161, 855, 206, 788, 266, 710, 340, 623,
    411, 548, 455, 455, 548, 366, 576, 288,
    122, 972, 159, 914, 211, 842, 276, 758,
    341, 682, 389, 584, 483, 483, 520, 390,
    110, 1172, 144, 1107, 193, 1028, 254, 932,
    317, 846, 366, 731, 458, 611, 499, 499,
  ];

  private readonly Mpeg4Frame _frame;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly byte[] _predictionTable;
  private readonly byte[] _scratch = new byte[41];
  private readonly int[] _block = new int[64];
  private readonly MsMpeg4VlcTable?[] _acTable = new MsMpeg4VlcTable?[4];
  private readonly MsMpeg4VlcTable?[] _dcTable = new MsMpeg4VlcTable?[3];
  private MsMpeg4VlcTable? _orientTable;

  private readonly int _quant;
  private readonly int _dquant;
  private readonly int _qsum;
  private readonly bool _loopFilter;
  private bool _useQuantMatrix;
  private readonly int _divideQuantDcLuma;
  private readonly int _quantDcChroma;
  private readonly int _divideQuantDcChroma;

  private int _x;
  private int _y;
  private int _edges;
  private int _orient;
  private int _rawOrient;
  private int _estRun;
  private int _chromaOrient;
  private bool _flatDc;
  private int _predictedDc;

  /// <param name="frame">The picture to fill.</param>
  /// <param name="macroblockWidth">Its width in macroblocks.</param>
  /// <param name="macroblockHeight">Its height in macroblocks.</param>
  /// <param name="quantiser">The picture header's quantiser.</param>
  /// <param name="loopFilter">Whether the extension header turned the deblocking filter on.</param>
  internal IntraX8Decoder(Mpeg4Frame frame, int macroblockWidth, int macroblockHeight, int quantiser, bool loopFilter) {
    this._frame = frame;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._predictionTable = new byte[macroblockWidth * 2 * 2];
    this._dquant = 2 * quantiser;
    this._quant = quantiser;
    this._qsum = (quantiser - 1) | 1;
    this._loopFilter = loopFilter;

    this._divideQuantDcLuma = ((1 << 16) + (this._quant >> 1)) / this._quant;
    if (this._quant < 5) {
      this._quantDcChroma = this._quant;
      this._divideQuantDcChroma = this._divideQuantDcLuma;
    } else {
      this._quantDcChroma = this._quant + ((this._quant + 3) >> 3);
      this._divideQuantDcChroma = ((1 << 16) + (this._quantDcChroma >> 1)) / this._quantDcChroma;
    }
  }

  /// <summary>Decodes the whole picture: <c>ff_intrax8_decode_picture</c>.</summary>
  internal void Decode(ref Mpeg4BitReader reader) {
    this._useQuantMatrix = reader.ReadBit() == 1;

    for (this._y = 0; this._y < this._macroblockHeight * 2; ++this._y) {
      if (reader.BitsRemaining < 1)
        throw new InvalidDataException($"This WMV2 J-frame ends before block row {this._y} of {this._macroblockHeight * 2}.");

      for (this._x = 0; this._x < this._macroblockWidth * 2; ++this._x) {
        this._GetPrediction();
        this._SetupSpatialPredictor(ref reader, 0);
        this._DecodeBlock(ref reader, 0);

        if ((this._x & this._y & 1) != 0) {
          this._GetChromaPrediction();
          this._SetupSpatialPredictor(ref reader, 1);
          this._DecodeBlock(ref reader, 1);
          this._SetupSpatialPredictor(ref reader, 2);
          this._DecodeBlock(ref reader, 2);
        }
      }
    }
  }

  // ============================================================================================
  // Where a block's samples are
  // ============================================================================================

  private (byte[] Plane, int Origin, int Stride) _Destination(int plane) {
    if (plane == 0)
      return (this._frame.Luma, this._frame.LumaOrigin + this._y * 8 * this._frame.LumaStride + this._x * 8, this._frame.LumaStride);

    var chroma = plane == 1 ? this._frame.Cb : this._frame.Cr;
    return (chroma, this._frame.ChromaOrigin + (this._y >> 1) * 8 * this._frame.ChromaStride + (this._x >> 1) * 8, this._frame.ChromaStride);
  }

  // ============================================================================================
  // Prediction
  // ============================================================================================

  /// <summary><c>x8_get_prediction</c>: the run estimate and direction a luminance block starts from.</summary>
  private void _GetPrediction() {
    this._edges = (this._x == 0 ? 1 : 0) | (this._y == 0 ? 2 : 0) | (this._x >= 2 * this._macroblockWidth - 1 ? 4 : 0);

    switch (this._edges & 3) {
      case 1:
        this._estRun = this._predictionTable[(this._y & 1) == 0 ? 1 : 0] >> 2;
        this._orient = 1;
        return;
      case 2:
        this._estRun = this._predictionTable[2 * this._x - 2] >> 2;
        this._orient = 2;
        return;
      case 3:
        this._estRun = 16;
        this._orient = 0;
        return;
    }

    var odd = this._y & 1;
    int b = this._predictionTable[2 * this._x + (1 - odd)];
    int a = this._predictionTable[2 * this._x - 2 + odd];
    int c = this._predictionTable[2 * this._x - 2 + (1 - odd)];

    this._estRun = Math.Min(b, a);
    // FFmpeg's comment: this has nothing to do with the edges, even if it looks like it — it triggers at
    // x = 3, y = 2 — and it is what every decoder does.
    if ((this._x & this._y) != 0)
      this._estRun = Math.Min(c, this._estRun);
    this._estRun >>= 2;

    a &= 3;
    b &= 3;
    c &= 3;

    var i = (int)((0xFFEAF4C4u >> (2 * b + 8 * a)) & 3);
    this._orient = i != 3 ? i : (int)((0xFFEAD8u >> (2 * c + 8 * (this._quant > 12 ? 1 : 0))) & 3);
  }

  /// <summary><c>x8_get_prediction_chroma</c>.</summary>
  private void _GetChromaPrediction() {
    this._edges = ((this._x >> 1) == 0 ? 1 : 0) | ((this._y >> 1) == 0 ? 2 : 0) | (this._x >= 2 * this._macroblockWidth - 1 ? 4 : 0);
    this._rawOrient = 0;
    if ((this._edges & 3) != 0) {
      this._chromaOrient = 4 << ((0xCC >> this._edges) & 1);
      return;
    }

    this._chromaOrient = (this._predictionTable[2 * this._x - 2] & 0x03) << 2;
  }

  /// <summary><c>x8_setup_spatial_predictor</c>: gathers the edge samples and settles the direction.</summary>
  private void _SetupSpatialPredictor(ref Mpeg4BitReader reader, int plane) {
    var (samples, origin, stride) = this._Destination(plane);
    var (range, sum) = this._SetupSpatialCompensation(samples, origin, stride);

    int quant;
    if (plane != 0) {
      this._orient = this._chromaOrient;
      quant = this._quantDcChroma;
    } else {
      quant = this._quant;
    }

    this._flatDc = false;
    if (range < quant || range < 3) {
      this._orient = 0;
      if (range < 3) {
        this._flatDc = true;
        sum += 9;
        this._predictedDc = sum * 6899 >> 17;
      }
    }

    if (plane != 0)
      return;

    if (range < 2 * this._quant) {
      if ((this._edges & 3) == 0) {
        if (this._orient == 1)
          this._orient = 11;
        if (this._orient == 2)
          this._orient = 10;
      } else {
        this._orient = 0;
      }

      this._rawOrient = 0;
    } else {
      this._orientTable ??= _Orient[this._quant < 13 ? 2 + reader.ReadBits(2) : reader.ReadBits(1)];
      this._rawOrient = this._orientTable.Read(ref reader);
      this._orient = _PredictionTable[this._orient][this._rawOrient];
    }
  }

  /// <summary><c>x8_setup_spatial_compensation</c>: fills the scratch edge and measures its range and sum.</summary>
  private (int Range, int Sum) _SetupSpatialCompensation(byte[] plane, int origin, int stride) {
    var dst = this._scratch;
    if ((this._edges & 3) == 3) {
      dst.AsSpan().Fill(0x80);
      return (0, 0x80 * (8 + 1 + 8 + 2));
    }

    var minimum = 256;
    var maximum = -1;
    var sum = 0;

    if ((this._edges & 1) == 0) {
      var at = origin - 1;
      for (var i = 7; i >= 0; --i) {
        dst[_AREA1 + i] = plane[at - 1];
        int c = plane[at];
        sum += c;
        minimum = Math.Min(minimum, c);
        maximum = Math.Max(maximum, c);
        dst[_AREA2 + i] = (byte)c;
        at += stride;
      }
    }

    if ((this._edges & 2) == 0) {
      var top = origin - stride;
      int c = 0;
      for (var i = 0; i < 8; ++i) {
        c = plane[top + i];
        sum += c;
        minimum = Math.Min(minimum, c);
        maximum = Math.Max(maximum, c);
      }

      if ((this._edges & 4) != 0) {
        dst.AsSpan(_AREA5, 8).Fill((byte)c);
        plane.AsSpan(top, 8).CopyTo(dst.AsSpan(_AREA4));
      } else {
        plane.AsSpan(top, 16).CopyTo(dst.AsSpan(_AREA4));
      }

      plane.AsSpan(top - stride, 8).CopyTo(dst.AsSpan(_AREA6));
    }

    if ((this._edges & 3) != 0) {
      var average = (byte)((sum + 4) >> 3);
      if ((this._edges & 1) != 0)
        dst.AsSpan(_AREA1, 8 + 8 + 1).Fill(average);
      else
        dst.AsSpan(_AREA3, 1 + 16 + 8).Fill(average);

      sum += average * 9;
    } else {
      int corner = plane[origin - 1 - stride];
      dst[_AREA3] = (byte)corner;
      sum += corner;
    }

    sum += dst[_AREA5] + dst[_AREA5 + 1];
    return (maximum - minimum, sum);
  }

  private void _UpdatePredictions(int orient, int estRun)
    => this._predictionTable[this._x * 2 + (this._y & 1)] = (byte)((estRun << 2) + (orient == 4 ? 1 : 0) + (orient == 8 ? 2 : 0));

  // ============================================================================================
  // One block
  // ============================================================================================

  /// <summary><c>x8_decode_intra_mb</c>.</summary>
  private void _DecodeBlock(ref Mpeg4BitReader reader, int plane) {
    var chroma = plane != 0;
    var block = this._block;
    Array.Clear(block);

    var dcMode = chroma ? 2 : this._estRun != 0 ? 1 : 0;
    var (dcLevel, final) = this._ReadDc(ref reader, dcMode);
    var n = 0;
    var zerosOnly = false;
    var (samples, origin, stride) = this._Destination(plane);

    var placed = false;
    if (!final) {
      var useQuantMatrix = this._useQuantMatrix;
      int acMode, estRun;
      if (chroma) {
        acMode = 1;
        estRun = 64;
      } else {
        if (this._rawOrient < 3)
          useQuantMatrix = false;

        if (this._rawOrient > 4) {
          acMode = 0;
          estRun = 64;
        } else if (this._estRun > 1) {
          acMode = 2;
          estRun = this._estRun;
        } else {
          acMode = 3;
          estRun = 64;
        }
      }

      this._SelectAcTable(ref reader, acMode);
      var scan = ((0x928548 >> (2 * this._orient)) & 3) switch {
        0 => MsMpeg4Data.Wmv1InterScan,
        1 => MsMpeg4Data.Wmv1HorizontalScan,
        _ => MsMpeg4Data.Wmv1VerticalScan,
      };

      var position = 0;
      bool last;
      do {
        ++n;
        if (n >= estRun) {
          acMode = 3;
          this._SelectAcTable(ref reader, 3);
        }

        var (run, level, isLast) = this._ReadAc(ref reader, acMode);
        last = isLast;
        position += run + 1;
        if (position > 63)
          throw new InvalidDataException(
            $"A block of this WMV2 J-frame at ({this._x}, {this._y}) runs to coefficient {position}, past the 63 an 8x8 block holds.");

        level = (level + 1) * this._dquant + this._qsum;
        if (reader.ReadBit() == 1)
          level = -level;
        if (useQuantMatrix)
          level = (level * _QuantTable[position]) >> 8;

        block[scan[position]] = unchecked((short)level);
      } while (!last);
    } else if (this._flatDc && (uint)(dcLevel + 1) < 3) {
      var divideQuant = chroma ? this._divideQuantDcChroma : this._divideQuantDcLuma;
      var dcQuant = chroma ? this._quantDcChroma : this._quant;
      dcLevel += (this._predictedDc * divideQuant + (1 << 12)) >> 13;
      _Fill(samples, origin, stride, _Clip((dcLevel * dcQuant + 4) >> 3));
      placed = true;
    } else {
      zerosOnly = dcLevel == 0;
    }

    if (!placed) {
      block[0] = unchecked((short)(dcLevel * (chroma ? this._quantDcChroma : this._quant)));

      if ((uint)(dcLevel + 1) >= 3 && (this._edges & 3) != 3) {
        var direction = (0x6A017C >> (this._orient * 2)) & 3;
        if (direction != 3)
          _AcCompensation(block, direction, block[0]);
      }

      if (this._flatDc)
        _Fill(samples, origin, stride, (byte)this._predictedDc);
      else
        this._SpatialCompensation(this._orient, samples, origin, stride);

      if (!zerosOnly) {
        Wmv2Transform.Transform(block);
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x) {
            var at = origin + y * stride + x;
            samples[at] = _Clip(samples[at] + block[y * 8 + x]);
          }
      }
    }

    if (!chroma)
      this._UpdatePredictions(this._orient, n);

    if (this._loopFilter) {
      if (!((this._edges & 2) != 0 || (zerosOnly && (this._orient | 4) == 4)))
        _LoopFilter(samples, origin, stride, 1, this._quant);
      if (!((this._edges & 1) != 0 || (zerosOnly && (this._orient | 8) == 8)))
        _LoopFilter(samples, origin, 1, stride, this._quant);
    }
  }

  /// <summary><c>x8_ac_compensation</c>: the first row and column a directional prediction leaves behind, taken off.</summary>
  private static void _AcCompensation(int[] block, int direction, int dcLevel) {
    int T(int x) => (x * dcLevel + 0x8000) >> 16;
    void Sub(int x, int y, int t) => block[x + y * 8] = unchecked((short)(block[x + y * 8] - t));
    void Add(int x, int y, int t) => block[x + y * 8] = unchecked((short)(block[x + y * 8] + t));

    switch (direction) {
      case 0: {
        var t = T(3811);
        Sub(1, 0, t);
        Sub(0, 1, t);
        t = T(487);
        Sub(2, 0, t);
        Sub(0, 2, t);
        t = T(506);
        Sub(3, 0, t);
        Sub(0, 3, t);
        t = T(135);
        Sub(4, 0, t);
        Sub(0, 4, t);
        Add(2, 1, t);
        Add(1, 2, t);
        Add(3, 1, t);
        Add(1, 3, t);
        t = T(173);
        Sub(5, 0, t);
        Sub(0, 5, t);
        t = T(61);
        Sub(6, 0, t);
        Sub(0, 6, t);
        Add(5, 1, t);
        Add(1, 5, t);
        t = T(42);
        Sub(7, 0, t);
        Sub(0, 7, t);
        Add(4, 1, t);
        Add(1, 4, t);
        Add(4, 4, t);
        t = T(1084);
        Add(1, 1, t);
        break;
      }
      case 1:
        Sub(0, 1, T(6269));
        Sub(0, 3, T(708));
        Sub(0, 5, T(172));
        Sub(0, 7, T(73));
        break;
      case 2:
        Sub(1, 0, T(6269));
        Sub(3, 0, T(708));
        Sub(5, 0, T(172));
        Sub(7, 0, T(73));
        break;
    }
  }

  // ============================================================================================
  // Codes
  // ============================================================================================

  private void _SelectAcTable(ref Mpeg4BitReader reader, int mode) {
    if (this._acTable[mode] != null)
      return;
    var index = reader.ReadBits(3);
    this._acTable[mode] = _Ac[((this._quant < 13 ? 1 : 0) * 2 + (mode >> 1)) * 8 + index];
  }

  /// <summary><c>x8_get_dc_rlf</c>.</summary>
  private (int Level, bool Final) _ReadDc(ref Mpeg4BitReader reader, int mode) {
    this._dcTable[mode] ??= _Dc[(this._quant < 13 ? 1 : 0) * 8 + reader.ReadBits(3)];
    var i = this._dcTable[mode]!.Read(ref reader);

    var final = i > 16;
    if (final)
      i -= 17;
    if (i == 0)
      return (0, final);

    var c = (i + 1) >> 1;
    if (c > 1)
      --c;
    var e = reader.ReadBits(c);
    i = _DcIndexOffset[i] + (e >> 1);
    return ((e & 1) != 0 ? -i : i, final);
  }

  /// <summary><c>x8_get_ac_rlf</c>: a run, a level counted from nought, and whether it is the block's last.</summary>
  private (int Run, int Level, bool Final) _ReadAc(ref Mpeg4BitReader reader, int mode) {
    var i = this._acTable[mode]!.Read(ref reader);

    if (i < 46) {
      var final = i > 22;
      if (final)
        i -= 23;
      var l = (0xE50000 >> (i & 0x1E)) & 3;
      var t = 0x01030F >> (l << 3);
      return (i & t, l, final);
    }

    if (i < 73) {
      i -= 46;
      var sm = _AcDecode[i];
      var e = (uint)reader.ReadBits((int)(sm & 0xF));
      sm >>= 8;
      var mask = sm & 0xFF;
      sm >>= 8;
      return ((int)((sm & 0xFF) + (e & mask)), (int)((sm >> 8) + (e & ~mask)), i > 58 - 46);
    }

    if (i < 75) {
      var e = reader.ReadBits(5);
      return (_CrazyMixRunLevel[e] >> 4, _CrazyMixRunLevel[e] & 0x0F, (i & 1) == 0);
    }

    var level = reader.ReadBits(7 - 3 * (i & 1));
    var run = reader.ReadBits(6);
    return (run, level, reader.ReadBit() == 1);
  }

  // ============================================================================================
  // Spatial prediction and filtering: intrax8dsp.c
  // ============================================================================================

  private void _SpatialCompensation(int orient, byte[] plane, int origin, int stride) {
    var src = this._scratch;
    for (var y = 0; y < 8; ++y) {
      var row = origin + y * stride;
      switch (orient) {
        case 0:
          _SpatialZero(src, plane, origin, stride);
          return;
        case 1:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = src[_AREA4 + Math.Min(2 * y + x + 2, 15)];
          break;
        case 2:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = src[_AREA4 + 1 + y + x];
          break;
        case 3:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = src[_AREA4 + ((y + 1) >> 1) + x];
          break;
        case 4:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = (byte)((src[_AREA4 + x] + src[_AREA6 + x] + 1) >> 1);
          break;
        case 5:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = 2 * x - y < 0 ? src[_AREA2 + 9 + 2 * x - y] : src[_AREA4 + x - ((y + 1) >> 1)];
          break;
        case 6:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = src[_AREA3 - y + x];
          break;
        case 7:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = x - 2 * y > 0
              ? (byte)((src[_AREA3 - 1 + x - 2 * y] + src[_AREA3 + x - 2 * y] + 1) >> 1)
              : src[_AREA2 + 8 - y + (x >> 1)];
          break;
        case 8:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = (byte)((src[_AREA1 + 7 - y] + src[_AREA2 + 7 - y] + 1) >> 1);
          break;
        case 9:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = src[_AREA2 + 6 - Math.Min(x + y, 6)];
          break;
        case 10:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = (byte)((src[_AREA2 + 7 - y] * (8 - x) + src[_AREA4 + x] * x + 4) >> 3);
          break;
        default:
          for (var x = 0; x < 8; ++x)
            plane[row + x] = (byte)((src[_AREA2 + 7 - y] * y + src[_AREA4 + x] * (8 - y) + 4) >> 3);
          break;
      }
    }
  }

  /// <summary><c>spatial_compensation_0</c>: the smooth predictor, a weighted blend of the whole edge.</summary>
  private static void _SpatialZero(byte[] src, byte[] plane, int origin, int stride) {
    Span<ushort> leftSum = stackalloc ushort[16];
    Span<ushort> topSum = stackalloc ushort[16];
    leftSum.Clear();
    topSum.Clear();

    unchecked {
      for (var i = 0; i < 8; ++i) {
        var a = src[_AREA2 + 7 - i] << 4;
        for (var j = 0; j < 8; ++j) {
          var p = Math.Abs(i - j);
          leftSum[(p & 1) * 8 + j] += (ushort)(a >> (p >> 1));
        }
      }

      for (var i = 0; i < 12; ++i) {
        var a = src[_AREA4 + i] << 4;
        var from = i < 8 ? 0 : i < 10 ? 5 : 7;
        for (var j = from; j < 8; ++j) {
          var p = Math.Abs(i - j);
          topSum[(p & 1) * 8 + j] += (ushort)(a >> (p >> 1));
        }
      }

      for (var i = 0; i < 8; ++i) {
        topSum[i] += (ushort)((topSum[8 + i] * 181 + 128) >> 8);
        leftSum[i] += (ushort)((leftSum[8 + i] * 181 + 128) >> 8);
      }

      for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x)
          plane[origin + y * stride + x] = (byte)(((uint)topSum[x] * _ZeroPredictionWeights[y * 16 + x * 2]
            + (uint)leftSum[y] * _ZeroPredictionWeights[y * 16 + x * 2 + 1] + 0x8000) >> 16);
    }
  }

  /// <summary><c>x8_loop_filter</c>: across an edge whose samples are <paramref name="across"/> apart, eight lines <paramref name="along"/> apart.</summary>
  private static void _LoopFilter(byte[] plane, int origin, int across, int along, int quant) {
    var ql = (quant + 10) >> 3;
    var ptr = origin;
    for (var i = 0; i < 8; ++i, ptr += along) {
      int p0 = plane[ptr - 5 * across];
      int p1 = plane[ptr - 4 * across];
      int p2 = plane[ptr - 3 * across];
      int p3 = plane[ptr - 2 * across];
      int p4 = plane[ptr - across];
      int p5 = plane[ptr];
      int p6 = plane[ptr + across];
      int p7 = plane[ptr + 2 * across];
      int p8 = plane[ptr + 3 * across];
      int p9 = plane[ptr + 4 * across];

      var t = (Math.Abs(p1 - p2) <= ql ? 1 : 0) + (Math.Abs(p2 - p3) <= ql ? 1 : 0)
        + (Math.Abs(p3 - p4) <= ql ? 1 : 0) + (Math.Abs(p4 - p5) <= ql ? 1 : 0);

      if (t > 0) {
        t += (Math.Abs(p5 - p6) <= ql ? 1 : 0) + (Math.Abs(p6 - p7) <= ql ? 1 : 0)
          + (Math.Abs(p7 - p8) <= ql ? 1 : 0) + (Math.Abs(p8 - p9) <= ql ? 1 : 0)
          + (Math.Abs(p0 - p1) <= ql ? 1 : 0);
        if (t >= 6) {
          var min = Math.Min(Math.Min(p1, p3), Math.Min(p5, p8));
          var max = Math.Max(Math.Max(p1, p3), Math.Max(p5, p8));
          if (max - min < 2 * quant) {
            min = Math.Min(min, Math.Min(Math.Min(p2, p4), Math.Min(p6, p7)));
            max = Math.Max(max, Math.Max(Math.Max(p2, p4), Math.Max(p6, p7)));
            if (max - min < 2 * quant) {
              plane[ptr - 2 * across] = (byte)((4 * p2 + 3 * p3 + 1 * p7 + 4) >> 3);
              plane[ptr - across] = (byte)((3 * p2 + 3 * p4 + 2 * p7 + 4) >> 3);
              plane[ptr] = (byte)((2 * p2 + 3 * p5 + 3 * p7 + 4) >> 3);
              plane[ptr + across] = (byte)((1 * p2 + 3 * p6 + 4 * p7 + 4) >> 3);
              continue;
            }
          }
        }
      }

      var x0 = (2 * p3 - 5 * p4 + 5 * p5 - 2 * p6 + 4) >> 3;
      if (Math.Abs(x0) < quant) {
        var x1 = (2 * p1 - 5 * p2 + 5 * p3 - 2 * p4 + 4) >> 3;
        var x2 = (2 * p5 - 5 * p6 + 5 * p7 - 2 * p8 + 4) >> 3;

        var x = Math.Abs(x0) - Math.Min(Math.Abs(x1), Math.Abs(x2));
        var m = p4 - p5;

        if (x > 0 && (m ^ x0) < 0) {
          var sign = m >> 31;
          m = (m ^ sign) - sign;
          m >>= 1;
          x = 5 * x >> 3;
          if (x > m)
            x = m;
          x = (x ^ sign) - sign;

          plane[ptr - across] = unchecked((byte)(plane[ptr - across] - x));
          plane[ptr] = unchecked((byte)(plane[ptr] + x));
        }
      }
    }
  }

  private static void _Fill(byte[] plane, int origin, int stride, byte value) {
    for (var y = 0; y < 8; ++y)
      plane.AsSpan(origin + y * stride, 8).Fill(value);
  }

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  // ============================================================================================
  // Tables
  // ============================================================================================

  private static uint _E(int extraBits, bool extraRun, int runOffset, int levelOffset)
    => (uint)extraBits | (extraRun ? 0xFFu << 8 : 0u) | ((uint)runOffset << 16) | ((uint)levelOffset << 24);

  private static MsMpeg4VlcTable[] _Build(byte[] pairs, int symbols, int tables, string name) {
    var result = new MsMpeg4VlcTable[tables];
    for (var t = 0; t < tables; ++t) {
      var lengths = new int[symbols];
      var values = new int[symbols];
      for (var s = 0; s < symbols; ++s) {
        values[s] = pairs[(t * symbols + s) * 2];
        lengths[s] = pairs[(t * symbols + s) * 2 + 1];
      }

      result[t] = MsMpeg4VlcTable.FromLengths(name, lengths, values);
    }

    return result;
  }

  /// <summary>The two high-quantiser orientation tables, then the four low-quantiser ones.</summary>
  private static MsMpeg4VlcTable[] _BuildOrient() {
    var high = _Build(IntraX8Tables.OrientHighQuant, 12, 2, "an IntraX8 orientation table");
    var low = _Build(IntraX8Tables.OrientLowQuant, 12, 4, "an IntraX8 orientation table");
    return [.. high, .. low];
  }
}
