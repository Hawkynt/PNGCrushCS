using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Wmv2;

/// <summary>What a WMV2 stream's four-byte extension header fixes for every picture.</summary>
/// <param name="QuarterSampleMotion">Whether a predicted picture may switch its luminance motion to the four-tap quarter-sample filter.</param>
/// <param name="LoopFilter">Whether pictures are deblocked.</param>
/// <param name="AdaptiveTransform">Whether a predicted block may be coded as two 8x4 or two 4x8 halves.</param>
/// <param name="JFrames">Whether an intra picture states if it is an IntraX8 picture.</param>
/// <param name="TopLeftVector">Whether a macroblock may choose its vector predictor where the neighbours disagree.</param>
/// <param name="PerMacroblockRunLevel">Whether a picture may restate its run-level table per macroblock.</param>
/// <param name="SliceCount">How many slices a picture is cut into.</param>
internal sealed record Wmv2Extension(
  bool QuarterSampleMotion, bool LoopFilter, bool AdaptiveTransform, bool JFrames,
  bool TopLeftVector, bool PerMacroblockRunLevel, int SliceCount) {

  /// <summary>Reads the 32 bits FFmpeg's <c>decode_ext_header</c> reads; frame rate and bit rate are skipped.</summary>
  internal static Wmv2Extension Parse(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException(
        $"A WMV2 stream needs the four-byte extension header its container carries after the BITMAPINFOHEADER; {data.Length} byte(s) were there.");
    var reader = new Mpeg4BitReader(data[..4]);
    reader.Skip(5 + 11);
    var extension = new Wmv2Extension(
      reader.ReadBit() == 1, reader.ReadBit() == 1, reader.ReadBit() == 1, reader.ReadBit() == 1,
      reader.ReadBit() == 1, reader.ReadBit() == 1, reader.ReadBits(3));
    if (extension.SliceCount == 0)
      throw new InvalidDataException("This WMV2 extension header states nought slices a picture.");
    return extension;
  }
}

/// <summary>
/// Decodes one WMV2 picture: its header, then either IntraX8 or the macroblock layer.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/wmv2dec.c</c> (LGPL-2.1-or-later) on top of the package's
/// WMV1 block layer, which WMV2 keeps: the run-level tables, escapes, DC prediction and scans are
/// WMV1's. What WMV2 changes is all around it — its own transform; a choice of three tables for the
/// predicted macroblock type, made by the quantiser; a skip map stated up front for the whole picture
/// in one of three layouts; a vector predictor that may be told which neighbour to take; quarter-sample
/// motion; adaptively split transforms; the H.263 deblocking filter; and the IntraX8 picture.
/// </remarks>
internal sealed class Wmv2PictureDecoder {

  private const int _SKIP_NONE = 0;
  private const int _SKIP_MPEG = 1;
  private const int _SKIP_ROW = 2;

  private static readonly MsMpeg4VlcTable[] _MacroblockTables = [
    MsMpeg4VlcTable.FromCodes("WMV2's first predicted macroblock table", Wmv2Data.NonIntra2Codes, Wmv2Data.NonIntra2Lengths),
    MsMpeg4VlcTable.FromCodes("WMV2's second predicted macroblock table", Wmv2Data.NonIntra3Codes, Wmv2Data.NonIntra3Lengths),
    MsMpeg4VlcTable.FromCodes("WMV2's third predicted macroblock table", Wmv2Data.NonIntra4Codes, Wmv2Data.NonIntra4Lengths),
  ];

  private static readonly int[] _SubPattern = [2, 3, 1];

  private readonly Wmv2Extension _extension;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _sliceHeight;

  // Per picture.
  private Mpeg4Frame _target = null!;
  private Mpeg4Frame? _reference;
  private int _quantiser;
  private int _rounding;
  private bool _mspel;
  private int _cbpTable;
  private bool _perMacroblockAbt;
  private bool _perBlockAbt;
  private int _abtType;
  private bool _perMacroblockRunLevel;
  private bool[] _skipped = null!;
  private short[] _vectorX = null!;
  private short[] _vectorY = null!;
  private MsMpeg4BlockDecoder _blocks = null!;
  private MsMpeg4IntraPrediction _intraPrediction = null!;
  private MsMpeg4CodedBlockPrediction? _codedBlock;
  private int _quarterShift;
  private int _vectorTable;

  internal Wmv2PictureDecoder(Wmv2Extension extension, int width, int height) {
    this._extension = extension;
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._sliceHeight = this._macroblockHeight / extension.SliceCount;
    if (this._sliceHeight == 0)
      throw new InvalidDataException(
        $"This WMV2 extension header cuts a picture {this._macroblockHeight} macroblock rows tall into {extension.SliceCount} slices, "
        + "and a slice is at least one row.");
  }

  /// <summary>The rounding the next predicted picture's half-sample averages use: FFmpeg's <c>no_rounding</c>, which every predicted picture flips.</summary>
  internal int Rounding { get; private set; }

  /// <summary>
  /// Decodes one picture against <paramref name="reference"/>, or returns <c>null</c> for a predicted
  /// picture whose skip map skips everything — which FFmpeg treats as no picture at all.
  /// </summary>
  internal Mpeg4Frame? Decode(ReadOnlySpan<byte> data, Mpeg4Frame? reference) {
    var reader = new Mpeg4BitReader(data);
    var intra = reader.ReadBit() == 0;
    if (intra)
      reader.Skip(7);
    this._quantiser = reader.ReadBits(5);
    if (this._quantiser == 0)
      throw new InvalidDataException("This WMV2 picture states a quantiser of nought.");

    if (!intra) {
      if (reference == null)
        throw new InvalidDataException("This WMV2 stream begins with a predicted picture; decoding has to start at an intra picture.");
      if (reader.NextBits(1) == 1 && this._IsWholePictureSkip(reader))
        return null;
    }

    this._reference = reference;
    this._target = new(this._macroblockWidth, this._macroblockHeight);
    var count = this._macroblockWidth * this._macroblockHeight;
    this._skipped = new bool[count];
    this._vectorX = new short[count];
    this._vectorY = new short[count];
    this._abtType = 0;
    this._perBlockAbt = false;
    this._mspel = false;

    int runLevel = 0, chromaRunLevel = 0, dcTable, vectorTable = 0;
    if (intra) {
      var jFrame = this._extension.JFrames && reader.ReadBit() == 1;
      this.Rounding = 1;
      if (jFrame) {
        new IntraX8Decoder(this._target, this._macroblockWidth, this._macroblockHeight, this._quantiser, this._extension.LoopFilter)
          .Decode(ref reader);
        this._target.PadBorders();
        return this._target;
      }

      this._perMacroblockRunLevel = this._extension.PerMacroblockRunLevel && reader.ReadBit() == 1;
      if (!this._perMacroblockRunLevel) {
        chromaRunLevel = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
        runLevel = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
      }

      dcTable = reader.ReadBit();
      if (reader.BitsRemaining * 8L < (long)this._macroblockWidth * this._macroblockHeight)
        throw new InvalidDataException(
          $"This WMV2 intra picture has {reader.BitsRemaining} bit(s) left for {count} macroblocks; FFmpeg refuses a picture that small.");
    } else {
      this._ReadSkipMap(ref reader);
      var stated = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
      this._cbpTable = Wmv2Data.CbpTableMap[3 * ((this._quantiser > 10 ? 1 : 0) + (this._quantiser > 20 ? 1 : 0)) + stated];
      this._mspel = this._extension.QuarterSampleMotion && reader.ReadBit() == 1;

      if (this._extension.AdaptiveTransform) {
        this._perMacroblockAbt = reader.ReadBit() == 0;
        if (!this._perMacroblockAbt)
          this._abtType = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
      }

      this._perMacroblockRunLevel = this._extension.PerMacroblockRunLevel && reader.ReadBit() == 1;
      if (!this._perMacroblockRunLevel) {
        runLevel = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
        chromaRunLevel = runLevel;
      }

      if (reader.BitsRemaining < 2)
        throw new InvalidDataException("This WMV2 predicted picture ends inside its header.");
      dcTable = reader.ReadBit();
      vectorTable = reader.ReadBit();
      this._vectorTable = vectorTable;
      this.Rounding ^= 1;
    }

    this._rounding = this.Rounding;
    var header = new MsMpeg4PictureHeader {
      CodingType = intra ? MsMpeg4PictureHeader.IntraCoded : MsMpeg4PictureHeader.PredictiveCoded,
      Quantiser = this._quantiser,
      SliceHeight = this._sliceHeight,
      SkipBitsArePresent = false,
      RunLevelTableIndex = runLevel,
      ChromaRunLevelTableIndex = chromaRunLevel,
      DcTableIndex = dcTable,
      MotionVectorTableIndex = vectorTable,
      PerMacroblockRunLevel = this._perMacroblockRunLevel,
    };

    this._blocks = new(MsMpeg4Version.Wmv2, header);
    this._intraPrediction = new(this._macroblockWidth, this._macroblockHeight, this._sliceHeight, wmv1: true);
    this._codedBlock = intra ? new(this._macroblockWidth, this._macroblockHeight) : null;

    for (var row = 0; row < this._macroblockHeight; ++row)
      for (var column = 0; column < this._macroblockWidth; ++column) {
        var address = row * this._macroblockWidth + column;
        if (reader.BitsRemaining <= 0 && !this._skipped[address])
          throw new InvalidDataException(
            $"This WMV2 picture ends before macroblock {address} of {count}. The packet is truncated or damaged; FFmpeg "
            + "conceals such a picture, and this decoder does not guess.");

        if (intra)
          this._DecodeIntraPictureMacroblock(ref reader, address);
        else
          this._DecodePredictedPictureMacroblock(ref reader, address);
      }

    if (reader.BitsRemaining < 0)
      throw new InvalidDataException(
        $"This WMV2 picture's macroblocks run {-reader.BitsRemaining} bit(s) past the end of its {data.Length}-byte packet.");

    if (this._extension.LoopFilter)
      Wmv2LoopFilter.Apply(this._target, this._macroblockWidth, this._macroblockHeight, this._quantiser, intra ? null : this._skipped);

    this._target.PadBorders();
    return this._target;
  }

  // ============================================================================================
  // The skip map
  // ============================================================================================

  /// <summary>
  /// <c>wmv2_decode_picture_header</c>'s look ahead: a row or column skip map whose every line says
  /// "all skipped" makes the picture no picture.
  /// </summary>
  private bool _IsWholePictureSkip(Mpeg4BitReader reader) {
    var type = reader.ReadBits(2);
    var run = type == 3 ? this._macroblockWidth : this._macroblockHeight;
    while (run > 0) {
      var block = Math.Min(run, 25);
      if (reader.BitsRemaining < block || reader.ReadBits(block) + 1 != 1 << block)
        return false;
      run -= block;
    }

    return true;
  }

  /// <summary><c>parse_mb_skip</c>.</summary>
  private void _ReadSkipMap(ref Mpeg4BitReader reader) {
    var type = reader.ReadBits(2);
    var width = this._macroblockWidth;
    var height = this._macroblockHeight;
    switch (type) {
      case _SKIP_NONE:
        break;

      case _SKIP_MPEG:
        if (reader.BitsRemaining < width * height)
          throw new InvalidDataException("This WMV2 picture's skip map is longer than the picture.");
        for (var i = 0; i < width * height; ++i)
          this._skipped[i] = reader.ReadBit() == 1;
        break;

      case _SKIP_ROW:
        for (var y = 0; y < height; ++y)
          if (reader.ReadBit() == 1) {
            for (var x = 0; x < width; ++x)
              this._skipped[y * width + x] = true;
          } else {
            for (var x = 0; x < width; ++x)
              this._skipped[y * width + x] = reader.ReadBit() == 1;
          }
        break;

      default:
        for (var x = 0; x < width; ++x)
          if (reader.ReadBit() == 1) {
            for (var y = 0; y < height; ++y)
              this._skipped[y * width + x] = true;
          } else {
            for (var y = 0; y < height; ++y)
              this._skipped[y * width + x] = reader.ReadBit() == 1;
          }
        break;
    }

    var coded = 0;
    foreach (var skipped in this._skipped)
      if (!skipped)
        ++coded;
    if (coded > reader.BitsRemaining)
      throw new InvalidDataException($"This WMV2 picture codes {coded} macroblocks in {reader.BitsRemaining} bits.");
  }

  // ============================================================================================
  // Macroblocks
  // ============================================================================================

  private void _DecodeIntraPictureMacroblock(ref Mpeg4BitReader reader, int address) {
    var code = MsMpeg4Tables.IntraMacroblockPattern.Read(ref reader);
    var pattern = 0;
    for (var index = 0; index < 6; ++index) {
      var bit = (code >> (5 - index)) & 1;
      if (index < 4) {
        bit ^= this._codedBlock!.Predict(address, index);
        this._codedBlock.Record(address, index, bit);
      }

      pattern |= bit << (5 - index);
    }

    this._DecodeIntra(ref reader, address, pattern);
  }

  private void _DecodeIntra(ref Mpeg4BitReader reader, int address, int pattern) {
    var predictCoefficients = reader.ReadBit() == 1;
    if (this._perMacroblockRunLevel && pattern != 0)
      this._blocks.UseRunLevelTable(MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader));

    Span<int> block = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._blocks.ReadIntra(ref reader, block, this._intraPrediction, address, index, predictCoefficients, _IsCoded(pattern, index));
      this._Store(address, index, block);
    }
  }

  private void _DecodePredictedPictureMacroblock(ref Mpeg4BitReader reader, int address) {
    Span<int> prediction = stackalloc int[6 * 64];

    if (this._skipped[address]) {
      this._intraPrediction.MarkUnavailable(address);
      this._Predict(prediction, address, 0, 0, 0);
      for (var index = 0; index < 6; ++index)
        this._Store(address, index, prediction.Slice(index * 64, 64));
      return;
    }

    var code = _MacroblockTables[this._cbpTable].Read(ref reader);
    var pattern = code & 0x3F;
    if ((code & 0x40) == 0) {
      this._DecodeIntra(ref reader, address, pattern);
      return;
    }

    this._intraPrediction.MarkUnavailable(address);
    var (predictedX, predictedY) = this._PredictVector(ref reader, address);

    if (pattern != 0) {
      if (this._perMacroblockRunLevel)
        this._blocks.UseRunLevelTable(MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader));

      if (this._extension.AdaptiveTransform && this._perMacroblockAbt) {
        this._perBlockAbt = reader.ReadBit() == 1;
        if (!this._perBlockAbt)
          this._abtType = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);
      } else {
        this._perBlockAbt = false;
      }
    }

    var (vectorX, vectorY) = this._ReadVector(ref reader, predictedX, predictedY);
    this._quarterShift = ((vectorX | vectorY) & 1) != 0 && this._mspel ? reader.ReadBit() : 0;
    this._vectorX[address] = (short)vectorX;
    this._vectorY[address] = (short)vectorY;

    Span<int> first = stackalloc int[64];
    Span<int> second = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    this._Predict(prediction, address, vectorX, vectorY, this._quarterShift);

    for (var index = 0; index < 6; ++index) {
      var samples = prediction.Slice(index * 64, 64);
      if (_IsCoded(pattern, index)) {
        if (this._perBlockAbt)
          this._abtType = MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader);

        first.Clear();
        if (this._abtType == 0) {
          this._blocks.ReadInterLevels(ref reader, first, MsMpeg4Data.Wmv1InterScan);
          Wmv2Transform.Transform(first);
          first.CopyTo(residual);
        } else {
          second.Clear();
          var scan = this._abtType == 1 ? Wmv2Data.ScanA : Wmv2Data.ScanB;
          var sub = _SubPattern[MsMpeg4PictureHeader.ReadZeroOneOrTwo(ref reader)];
          if ((sub & 1) != 0)
            this._blocks.ReadInterLevels(ref reader, first, scan);
          if ((sub & 2) != 0)
            this._blocks.ReadInterLevels(ref reader, second, scan);

          if (this._abtType == 1) {
            Wmv2Transform.Transform84(first, residual);
            Wmv2Transform.Transform84(second, residual[32..]);
          } else {
            Span<int> left = stackalloc int[64];
            Span<int> right = stackalloc int[64];
            Wmv2Transform.Transform48(first, left);
            Wmv2Transform.Transform48(second, right);
            for (var y = 0; y < 8; ++y)
              for (var x = 0; x < 4; ++x) {
                residual[y * 8 + x] = left[y * 8 + x];
                residual[y * 8 + 4 + x] = right[y * 8 + x];
              }
          }
        }

        for (var i = 0; i < 64; ++i)
          samples[i] += residual[i];
      }

      this._Store(address, index, samples);
    }
  }

  // ============================================================================================
  // Motion
  // ============================================================================================

  /// <summary><c>wmv2_pred_motion</c>.</summary>
  private (int X, int Y) _PredictVector(ref Mpeg4BitReader reader, int address) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var firstSliceLine = row % this._sliceHeight == 0;

    int ax = 0, ay = 0, bx = 0, by = 0, cx = 0, cy = 0;
    if (column > 0) {
      ax = this._vectorX[address - 1];
      ay = this._vectorY[address - 1];
    }

    if (row > 0) {
      bx = this._vectorX[address - this._macroblockWidth];
      by = this._vectorY[address - this._macroblockWidth];
      if (column + 1 < this._macroblockWidth) {
        cx = this._vectorX[address - this._macroblockWidth + 1];
        cy = this._vectorY[address - this._macroblockWidth + 1];
      }
    }

    var difference = column > 0 && !firstSliceLine && !this._mspel && this._extension.TopLeftVector
      ? Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by))
      : 0;

    var type = difference >= 8 ? reader.ReadBit() : 2;
    return type switch {
      0 => (ax, ay),
      1 => (bx, by),
      _ => firstSliceLine ? (ax, ay) : (_Median(ax, bx, cx), _Median(ay, by, cy)),
    };
  }

  /// <summary><c>ff_msmpeg4_decode_motion</c>.</summary>
  private (int X, int Y) _ReadVector(ref Mpeg4BitReader reader, int predictedX, int predictedY) {
    var code = MsMpeg4Tables.MotionVector[this._vectorTable].Read(ref reader);
    var (x, y) = code != 0 ? (code >> 8, code & 0xFF) : (reader.ReadBits(6), reader.ReadBits(6));
    return (_Wrap(x + predictedX - 32), _Wrap(y + predictedY - 32));
  }

  private static int _Wrap(int vector) => vector <= -64 ? vector + 64 : vector >= 64 ? vector - 64 : vector;

  private static int _Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

  private void _Predict(Span<int> prediction, int address, int vectorX, int vectorY, int quarterShift) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var reference = this._reference!;

    if (this._mspel) {
      Wmv2MotionCompensation.Predict(
        reference, column, row, vectorX, vectorY, quarterShift, this._width, this._height, this._rounding, prediction);
      return;
    }

    var chromaX = Mpeg4MotionCompensation.ToChroma(4 * vectorX);
    var chromaY = Mpeg4MotionCompensation.ToChroma(4 * vectorY);
    for (var index = 0; index < 6; ++index) {
      var (plane, stride, origin, width, height) = reference.PlaneOf(index);
      var (left, top) = this._BlockOrigin(address, index);
      var border = index < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;
      var (x, y) = index < 4 ? (vectorX, vectorY) : (chromaX, chromaY);
      Mpeg4MotionCompensation.PredictHalfSample(
        prediction.Slice(index * 64, 64), plane, stride, origin, border, width, height, left, top, x, y, this._rounding);
    }
  }

  // ============================================================================================
  // Samples
  // ============================================================================================

  private void _Store(int address, int index, scoped ReadOnlySpan<int> samples) {
    var (plane, stride, origin, _, _) = this._target.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private static bool _IsCoded(int pattern, int index) => (pattern & (1 << (5 - index))) != 0;

  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
