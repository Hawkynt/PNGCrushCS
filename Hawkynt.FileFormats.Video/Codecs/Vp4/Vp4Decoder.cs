using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Vp4;

/// <summary>
/// Decodes On2 VP4 pictures: VP3's frame structure under VP4's own macroblock coding, motion-vector
/// codes, coefficient ordering, DC prediction, quantisation and motion-compensation filter.
/// </summary>
/// <remarks>
/// Converted from the <c>version &gt;= 2</c> paths of FFmpeg's <c>libavcodec/vp3.c</c>
/// (LGPL-2.1-or-later), with the tables of <c>vp4data.h</c> and <c>vp3data.h</c> in
/// <see cref="Vp4Tables"/>. Everything is held the way FFmpeg holds it — planes bottom row first, the
/// picture padded to whole macroblocks, blocks in Hilbert order inside each 32x32 superblock — so the
/// quirks come across with it: the DC predictor's left neighbours carry from the end of one superblock
/// row into the start of the next, and an end-of-block run of nought means every later block that
/// reaches that coefficient ends there.
/// <para/>
/// VP4 has no loop filter of its own over the reconstructed picture. Instead, a motion-compensated
/// prediction that straddles a block edge of the picture it comes from has that edge filtered in a
/// copy before it is used.
/// </remarks>
internal sealed class Vp4Decoder {

  private const int _MODE_INTER_NO_MV = 0;
  private const int _MODE_INTRA = 1;
  private const int _MODE_INTER_PLUS_MV = 2;
  private const int _MODE_INTER_LAST_MV = 3;
  private const int _MODE_INTER_PRIOR_LAST = 4;
  private const int _MODE_USING_GOLDEN = 5;
  private const int _MODE_GOLDEN_MV = 6;
  private const int _MODE_INTER_FOURMV = 7;
  private const int _MODE_COPY = 8;

  /// <summary><c>vp4_pred_block_type_map</c>: the DC predictor's kind for each coding mode.</summary>
  internal static readonly byte[] PredictionType = [1, 0, 1, 1, 1, 2, 2, 1];

  private static readonly MsMpeg4VlcTable _ModeCode = MsMpeg4VlcTable.FromLengths(
    "VP4's mode code", [.. Vp4Tables.ModeCodeLengths], [0, 1, 2, 3, 4, 5, 6, 7]);

  private static readonly MsMpeg4VlcTable[] _BlockPattern = [_Pattern(0), _Pattern(1)];
  private static readonly MsMpeg4VlcTable[] _Vectors = _BuildVectors();
  private static readonly MsMpeg4VlcTable[] _Coefficients = _BuildCoefficients();

  private readonly int _width;
  private readonly int _height;
  private readonly int[] _fragmentWidth = new int[2];
  private readonly int[] _fragmentHeight = new int[2];
  private readonly int[] _fragmentStart = new int[3];
  private readonly int _fragmentCount;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int[] _superblockWidth = new int[2];
  private readonly int[] _superblockHeight = new int[2];
  private readonly int[] _planeMacroblockWidth = new int[2];
  private readonly int[] _planeMacroblockHeight = new int[2];
  private readonly int _yuvMacroblockCount;

  private readonly byte[] _coding;
  private readonly short[] _dc;
  private readonly short[][] _coefficients;
  private readonly int[] _lastCoefficient;
  private readonly byte[] _macroblockCoding;
  private readonly byte[] _macroblockCoded;
  private readonly sbyte[][] _vectors;

  private byte[][]? _last;
  private byte[][]? _golden;
  private int _version = 3;
  private bool _keyFrame;
  private int _quality = -1;
  private readonly short[,,] _dequant = new short[2, 3, 64];
  private int[] _bounding = Vp4Dsp.BoundingValues(0);

  internal Vp4Decoder(int width, int height) {
    if (width < 18)
      throw new NotSupportedException($"VP4 needs a picture at least 18 samples wide; this one is {width}, which FFmpeg refuses too.");
    this._width = (width + 15) & ~15;
    this._height = (height + 15) & ~15;

    this._superblockWidth[0] = (this._width + 31) / 32;
    this._superblockHeight[0] = (this._height + 31) / 32;
    this._superblockWidth[1] = (this._width / 2 + 31) / 32;
    this._superblockHeight[1] = (this._height / 2 + 31) / 32;
    this._macroblockWidth = this._width / 16;
    this._macroblockHeight = this._height / 16;
    this._planeMacroblockWidth[0] = this._macroblockWidth;
    this._planeMacroblockHeight[0] = this._macroblockHeight;
    this._planeMacroblockWidth[1] = (this._width / 2 + 15) / 16;
    this._planeMacroblockHeight[1] = (this._height / 2 + 15) / 16;
    this._yuvMacroblockCount = this._macroblockWidth * this._macroblockHeight
      + 2 * this._planeMacroblockWidth[1] * this._planeMacroblockHeight[1];

    this._fragmentWidth[0] = this._width / 8;
    this._fragmentHeight[0] = this._height / 8;
    this._fragmentWidth[1] = this._fragmentWidth[0] >> 1;
    this._fragmentHeight[1] = this._fragmentHeight[0] >> 1;
    var luma = this._fragmentWidth[0] * this._fragmentHeight[0];
    var chroma = this._fragmentWidth[1] * this._fragmentHeight[1];
    this._fragmentStart[1] = luma;
    this._fragmentStart[2] = luma + chroma;
    this._fragmentCount = luma + 2 * chroma;

    this._coding = new byte[this._fragmentCount];
    this._dc = new short[this._fragmentCount];
    this._coefficients = new short[this._fragmentCount][];
    this._lastCoefficient = new int[this._fragmentCount];
    this._macroblockCoding = new byte[this._macroblockWidth * this._macroblockHeight];
    this._macroblockCoded = new byte[this._yuvMacroblockCount];
    this._vectors = [new sbyte[2 * luma], new sbyte[2 * chroma]];
  }

  /// <summary>The picture's width, padded to whole macroblocks.</summary>
  internal int CodedWidth => this._width;

  /// <summary>The picture's height, padded to whole macroblocks.</summary>
  internal int CodedHeight => this._height;

  /// <summary>Decodes one packet into its three planes, bottom row first.</summary>
  internal byte[][] Decode(ReadOnlySpan<byte> data) {
    var reader = new Mpeg4BitReader(data);
    this._keyFrame = reader.ReadBit() == 0;
    reader.Skip(1);
    var quality = reader.ReadBits(6);
    if (quality != this._quality) {
      this._quality = quality;
      this._bounding = Vp4Dsp.BoundingValues(Vp4Tables.FilterLimit[quality]);
      this._InitialiseDequantiser();
    }

    if (this._keyFrame) {
      reader.Skip(8); // width and height codes
      this._version = reader.ReadBits(5);
      if (this._version < 2)
        throw new NotSupportedException($"This key frame states VP3 bitstream version {this._version}; it is a VP3 stream, not VP4.");
      reader.Skip(1 + 2);
      reader.Skip(8 + 8 + 5 + 3 + 5 + 3 + 2); // macroblock rows and columns, their multipliers, two unknown bits
    } else if (this._golden == null) {
      throw new InvalidDataException("This VP4 stream begins with an inter frame; decoding has to start at a key frame.");
    }

    Array.Clear(this._coding);
    Array.Clear(this._dc);
    this._UnpackMacroblocks(ref reader);
    this._UnpackModes(ref reader);
    this._UnpackVectors(ref reader);
    this._UnpackCoefficients(ref reader);

    var current = new byte[3][];
    current[0] = new byte[this._width * this._height];
    current[1] = new byte[this._width * this._height / 4];
    current[2] = new byte[this._width * this._height / 4];
    if (this._keyFrame)
      this._golden = current;

    for (var plane = 0; plane < 3; ++plane)
      this._Render(plane, current);

    this._last = current;
    return current;
  }

  // ============================================================================================
  // Dequantisation: init_dequantizer with VP4's tables
  // ============================================================================================

  private void _InitialiseDequantiser() {
    var ac = Vp4Tables.AcScale[this._quality];
    for (var inter = 0; inter < 2; ++inter)
      for (var plane = 0; plane < 3; ++plane) {
        int dcScale = plane == 0 ? Vp4Tables.LumaDcScale[this._quality] : Vp4Tables.ChromaDcScale[this._quality];
        for (var i = 0; i < 64; ++i) {
          int coefficient = Vp4Tables.GenericDequant[i];
          var minimum = 8 << (inter + (i == 0 ? 1 : 0));
          var scale = i != 0 ? ac : dcScale;
          var bias = (1 + inter) * 3;
          var value = i == 0
            ? Math.Clamp(scale * coefficient / 100 * 4, minimum, 4096)
            : (scale * (coefficient - bias) / 100 + bias) * 4;
          this._dequant[inter, plane, _Transpose(i)] = (short)value;
        }
      }
  }

  private static int _Transpose(int i) => (i >> 3) | ((i & 7) << 3);

  // ============================================================================================
  // Which blocks are coded: vp4_unpack_macroblocks
  // ============================================================================================

  private void _UnpackMacroblocks(ref Mpeg4BitReader reader) {
    Array.Fill(this._macroblockCoding, (byte)_MODE_COPY);
    if (this._keyFrame)
      return;

    var count = this._yuvMacroblockCount;
    var partial = false;
    var bit = reader.ReadBit();
    for (var i = 0; i < count;) {
      var run = _MacroblockRun(ref reader, count);
      if (run > count - i)
        throw new InvalidDataException("A VP4 macroblock run overruns the picture.");
      this._macroblockCoded.AsSpan(i, run).Fill((byte)(2 * bit));
      i += run;
      bit ^= 1;
      partial |= bit != 0;
    }

    if (partial) {
      bit = reader.ReadBit();
      var run = _MacroblockRun(ref reader, count);
      for (var i = 0; i < count; ++i) {
        if (this._macroblockCoded[i] != 0)
          continue;
        if (run == 0) {
          bit ^= 1;
          run = _MacroblockRun(ref reader, count);
        }

        this._macroblockCoded[i] = (byte)bit;
        --run;
      }

      if (run != 0)
        throw new InvalidDataException("A VP4 partial-macroblock run overruns the picture.");
    }

    var table = 0;
    var index = 0;
    for (var plane = 0; plane < 3; ++plane) {
      var p = plane == 0 ? 0 : 1;
      for (var sbY = 0; sbY < this._superblockHeight[p]; ++sbY)
        for (var sbX = 0; sbX < this._superblockWidth[p]; ++sbX)
          for (var j = 0; j < 4; ++j) {
            var mbX = 2 * sbX + (j >> 1);
            var mbY = (2 * sbY + (j >> 1)) ^ (j & 1);
            if (mbX >= this._planeMacroblockWidth[p] || mbY >= this._planeMacroblockHeight[p])
              continue;

            var coded = this._macroblockCoded[index++];
            int pattern;
            if (coded == 2) {
              pattern = 0xF;
            } else if (coded == 1) {
              var v = _BlockPattern[table].Read(ref reader);
              table = Vp4Tables.BlockPatternSelector[v];
              pattern = v + 1;
            } else {
              pattern = 0;
            }

            for (var k = 0; k < 4; ++k) {
              var x = 2 * mbX + (k & 1);
              var y = 2 * mbY + (k >> 1);
              if (x >= this._fragmentWidth[p] || y >= this._fragmentHeight[p])
                continue;
              this._coding[this._fragmentStart[plane] + y * this._fragmentWidth[p] + x] =
                (pattern & (8 >> k)) != 0 ? (byte)_MODE_INTER_NO_MV : (byte)_MODE_COPY;
            }
          }
    }
  }

  /// <summary><c>vp4_get_mb_count</c>.</summary>
  private static int _MacroblockRun(ref Mpeg4BitReader reader, int limit) {
    var v = 1;
    int bits;
    while ((bits = reader.NextBits(9)) == 0x1FF) {
      reader.Skip(9);
      v += 256;
      if (v > limit)
        throw new InvalidDataException("A VP4 macroblock run is longer than the picture.");
    }

    if (bits < 0x100) {
      reader.Skip(1);
      return v;
    }

    if (bits < 0x180) {
      reader.Skip(2);
      return v + 1;
    }

    for (var n = 1; n <= 6; ++n)
      if (bits < 0x200 - (0x80 >> n)) {
        reader.Skip(2 + n);
        return v + (1 << n) + reader.ReadBits(n);
      }

    reader.Skip(2 + 7);
    return v + (1 << 7) + reader.ReadBits(7);
  }

  // ============================================================================================
  // Modes and vectors: unpack_modes and unpack_vectors
  // ============================================================================================

  private void _UnpackModes(ref Mpeg4BitReader reader) {
    if (this._keyFrame) {
      Array.Fill(this._coding, (byte)_MODE_INTRA);
      return;
    }

    var scheme = reader.ReadBits(3);
    Span<int> alphabet = stackalloc int[8];
    if (scheme == 0) {
      alphabet.Fill(_MODE_INTER_NO_MV);
      for (var i = 0; i < 8; ++i)
        alphabet[reader.ReadBits(3)] = i;
    } else if (scheme != 7) {
      for (var i = 0; i < 8; ++i)
        alphabet[i] = Vp4Tables.ModeAlphabet[(scheme - 1) * 8 + i];
    }

    var fw = this._fragmentWidth[0];
    for (var sbY = 0; sbY < this._superblockHeight[0]; ++sbY)
      for (var sbX = 0; sbX < this._superblockWidth[0]; ++sbX) {
        if (reader.BitsRemaining <= 0)
          throw new InvalidDataException("This VP4 frame ends inside its macroblock modes.");

        for (var j = 0; j < 4; ++j) {
          var mbX = 2 * sbX + (j >> 1);
          var mbY = 2 * sbY + (((j >> 1) + j) & 1);
          if (mbX >= this._macroblockWidth || mbY >= this._macroblockHeight)
            continue;
          var macroblock = mbY * this._macroblockWidth + mbX;

          var anyCoded = false;
          for (var k = 0; k < 4 && !anyCoded; ++k)
            anyCoded = this._coding[(2 * mbY + (k >> 1)) * fw + 2 * mbX + (k & 1)] != _MODE_COPY;
          if (!anyCoded) {
            this._macroblockCoding[macroblock] = _MODE_INTER_NO_MV;
            continue;
          }

          var mode = scheme == 7 ? reader.ReadBits(3) : alphabet[_ModeCode.Read(ref reader)];
          this._macroblockCoding[macroblock] = (byte)mode;
          for (var k = 0; k < 4; ++k) {
            var f = (2 * mbY + (k >> 1)) * fw + 2 * mbX + (k & 1);
            if (this._coding[f] != _MODE_COPY)
              this._coding[f] = (byte)mode;
          }

          var c = mbY * this._fragmentWidth[1] + mbX;
          for (var plane = 1; plane < 3; ++plane)
            if (this._coding[this._fragmentStart[plane] + c] != _MODE_COPY)
              this._coding[this._fragmentStart[plane] + c] = (byte)mode;
        }
      }
  }

  private void _UnpackVectors(ref Mpeg4BitReader reader) {
    if (this._keyFrame)
      return;

    Span<int> mx = stackalloc int[4];
    Span<int> my = stackalloc int[4];
    int lastX = 0, lastY = 0, priorX = 0, priorY = 0, goldX = 0, goldY = 0;
    var fw = this._fragmentWidth[0];

    for (var sbY = 0; sbY < this._superblockHeight[0]; ++sbY)
      for (var sbX = 0; sbX < this._superblockWidth[0]; ++sbX) {
        if (reader.BitsRemaining <= 0)
          throw new InvalidDataException("This VP4 frame ends inside its motion vectors.");

        for (var j = 0; j < 4; ++j) {
          var mbX = 2 * sbX + (j >> 1);
          var mbY = 2 * sbY + (((j >> 1) + j) & 1);
          if (mbX >= this._macroblockWidth || mbY >= this._macroblockHeight)
            continue;
          var mode = this._macroblockCoding[mbY * this._macroblockWidth + mbX];
          if (mode == _MODE_COPY)
            continue;

          switch (mode) {
            case _MODE_GOLDEN_MV:
              goldX = mx[0] = _Vector(ref reader, 0, goldX);
              goldY = my[0] = _Vector(ref reader, 1, goldY);
              break;

            case _MODE_INTER_PLUS_MV:
              mx[0] = _Vector(ref reader, 0, lastX);
              my[0] = _Vector(ref reader, 1, lastY);
              priorX = lastX;
              priorY = lastY;
              lastX = mx[0];
              lastY = my[0];
              break;

            case _MODE_INTER_FOURMV:
              priorX = lastX;
              priorY = lastY;
              for (var k = 0; k < 4; ++k) {
                var f = (2 * mbY + (k >> 1)) * fw + 2 * mbX + (k & 1);
                if (this._coding[f] != _MODE_COPY) {
                  mx[k] = _Vector(ref reader, 0, priorX);
                  my[k] = _Vector(ref reader, 1, priorY);
                  lastX = mx[k];
                  lastY = my[k];
                } else {
                  mx[k] = 0;
                  my[k] = 0;
                }
              }
              break;

            case _MODE_INTER_LAST_MV:
              mx[0] = lastX;
              my[0] = lastY;
              break;

            case _MODE_INTER_PRIOR_LAST:
              mx[0] = priorX;
              my[0] = priorY;
              priorX = lastX;
              priorY = lastY;
              lastX = mx[0];
              lastY = my[0];
              break;

            default:
              mx[0] = 0;
              my[0] = 0;
              break;
          }

          for (var k = 0; k < 4; ++k) {
            var f = (2 * mbY + (k >> 1)) * fw + 2 * mbX + (k & 1);
            var four = mode == _MODE_INTER_FOURMV;
            this._vectors[0][2 * f] = (sbyte)(four ? mx[k] : mx[0]);
            this._vectors[0][2 * f + 1] = (sbyte)(four ? my[k] : my[0]);
          }

          if (mode == _MODE_INTER_FOURMV) {
            mx[0] = _RoundShift(mx[0] + mx[1] + mx[2] + mx[3], 2);
            my[0] = _RoundShift(my[0] + my[1] + my[2] + my[3], 2);
          }

          if (this._version <= 2) {
            mx[0] = (mx[0] >> 1) | (mx[0] & 1);
            my[0] = (my[0] >> 1) | (my[0] & 1);
          }

          var c = mbY * this._fragmentWidth[1] + mbX;
          this._vectors[1][2 * c] = (sbyte)mx[0];
          this._vectors[1][2 * c + 1] = (sbyte)my[0];
        }
      }
  }

  /// <summary><c>vp4_get_mv</c>.</summary>
  private static int _Vector(ref Mpeg4BitReader reader, int axis, int last) {
    var v = _Vectors[axis * 7 + Vp4Tables.MvTableSelector[Math.Abs(last)]].Read(ref reader) - 31;
    return last < 0 ? -v : v;
  }

  /// <summary>FFmpeg's <c>RSHIFT</c>: a shift that rounds half away from nought.</summary>
  private static int _RoundShift(int a, int b) => a > 0 ? (a + ((1 << b) >> 1)) >> b : (a + ((1 << b) >> 1) - 1) >> b;

  // ============================================================================================
  // Coefficients and DC prediction: vp4_unpack_dct_coeffs
  // ============================================================================================

  private void _UnpackCoefficients(ref Mpeg4BitReader reader) {
    if (reader.BitsRemaining < 16)
      throw new InvalidDataException("This VP4 frame ends before its coefficient tables are chosen.");
    var dcY = reader.ReadBits(4);
    var dcC = reader.ReadBits(4);
    var acY = reader.ReadBits(4);
    var acC = reader.ReadBits(4);

    var tables = new MsMpeg4VlcTable[2][];
    for (var p = 0; p < 2; ++p) {
      tables[p] = new MsMpeg4VlcTable[64];
      var ac = p == 0 ? acY : acC;
      tables[p][0] = _Coefficients[p == 0 ? dcY : dcC];
      for (var i = 1; i < 64; ++i)
        tables[p][i] = _Coefficients[ac + 16 * (i <= 5 ? 1 : i <= 14 ? 2 : i <= 27 ? 3 : 4)];
    }

    var eobTracker = new int[64];
    var predictor = new Vp4DcPredictor(this._superblockWidth[0]);
    predictor.BeginPicture();

    for (var plane = 0; plane < 3; ++plane) {
      var p = plane == 0 ? 0 : 1;
      var fw = this._fragmentWidth[p];
      var fh = this._fragmentHeight[p];
      Array.Clear(eobTracker);
      predictor.BeginPlane(fw);

      for (var sbY = 0; sbY * 4 < fh; ++sbY)
        for (var sbX = 0; sbX * 4 < fw; ++sbX) {
          predictor.BeginSuperblock(sbX);
          for (var j = 0; j < 16; ++j) {
            int hx = Vp4Tables.HilbertOffset[2 * j];
            int hy = Vp4Tables.HilbertOffset[2 * j + 1];
            var x = 4 * sbX + hx;
            var y = 4 * sbY + hy;
            if (x >= fw || y >= fh)
              continue;
            var fragment = this._fragmentStart[plane] + y * fw + x;
            if (this._coding[fragment] == _MODE_COPY)
              continue;

            this._UnpackBlock(ref reader, tables[p], eobTracker, fragment);
            var type = PredictionType[this._coding[fragment]];
            this._dc[fragment] = unchecked((short)(this._dc[fragment] + predictor.Predict(hx, hy, type)));
            predictor.Record(hx, hy, type, this._dc[fragment]);
          }

          predictor.EndSuperblock(sbX);
        }
    }
  }

  /// <summary>
  /// <c>vp4_unpack_vlcs</c>: one block's tokens, kept as coefficients in scan order with the index the
  /// block ends at, which is what <c>vp3_dequant</c> would have pulled back out of the token lists.
  /// </summary>
  private void _UnpackBlock(ref Mpeg4BitReader reader, MsMpeg4VlcTable[] tables, int[] eobTracker, int fragment) {
    var coefficients = this._coefficients[fragment] ??= new short[64];
    Array.Clear(coefficients);
    var index = 0;

    while (eobTracker[index] == 0) {
      if (reader.BitsRemaining < 1)
        throw new InvalidDataException("This VP4 frame ends inside its coefficients.");
      var token = tables[index].Read(ref reader);

      if (token <= 6) {
        var run = Vp4Tables.EobRunBase[token] + (Vp4Tables.EobRunBits[token] != 0 ? reader.ReadBits(Vp4Tables.EobRunBits[token]) : 0);
        eobTracker[index] = run - 1;
        this._lastCoefficient[fragment] = index;
        return;
      }

      var extra = Vp4Tables.CoeffBits[token] != 0 ? reader.ReadBits(Vp4Tables.CoeffBits[token]) : 0;
      var coefficient = Vp4Tables.CoeffValues[token]![extra];
      var zeroRun = Vp4Tables.ZeroRunBase[token] + (Vp4Tables.ZeroRunBits[token] != 0 ? reader.ReadBits(Vp4Tables.ZeroRunBits[token]) : 0);

      if (zeroRun != 0) {
        if (index + zeroRun > 63)
          throw new InvalidDataException("A VP4 block's zero run carries a coefficient past its sixty-fourth.");
        index += zeroRun;
      } else if (index == 0) {
        this._dc[fragment] = coefficient;
      }

      if (index != 0)
        coefficients[index] = coefficient;
      ++index;
      if (index >= 64) {
        this._lastCoefficient[fragment] = 63;
        return;
      }
    }

    --eobTracker[index];
    this._lastCoefficient[fragment] = index;
  }

  // ============================================================================================
  // Reconstruction: render_slice and vp4_mc_loop_filter
  // ============================================================================================

  private void _Render(int plane, byte[][] current) {
    var p = plane == 0 ? 0 : 1;
    var fw = this._fragmentWidth[p];
    var fh = this._fragmentHeight[p];
    var width = fw * 8;
    var height = fh * 8;
    var output = current[plane];
    var block = new short[64];
    var window = new byte[9 * 9];

    for (var y = 0; y < fh; ++y)
      for (var x = 0; x < fw; ++x) {
        var fragment = this._fragmentStart[plane] + y * fw + x;
        var mode = this._coding[fragment];
        var first = 8 * y * width + 8 * x;

        if (mode == _MODE_COPY) {
          var last = this._last![plane];
          for (var r = 0; r < 8; ++r)
            Array.Copy(last, first + r * width, output, first + r * width, 8);
          continue;
        }

        if (mode != _MODE_INTRA) {
          var reference = mode is _MODE_USING_GOLDEN or _MODE_GOLDEN_MV ? this._golden![plane] : this._last![plane];
          int motionX = 0, motionY = 0, half = 0, diagonal = 0, sourceX = 8 * x, sourceY = 8 * y;
          var filtered = false;
          if (mode > _MODE_INTRA && mode != _MODE_USING_GOLDEN) {
            var vector = y * fw + x;
            int rawX = this._vectors[p][2 * vector];
            int rawY = this._vectors[p][2 * vector + 1];
            motionX = rawX;
            motionY = rawY;
            if (plane != 0) {
              motionX = (motionX >> 1) | (motionX & 1);
              motionY = (motionY >> 1) | (motionY & 1);
            }

            sourceX = (motionX >> 1) + 8 * x;
            sourceY = (motionY >> 1) + 8 * y;
            half = (motionX & 1) | ((motionY & 1) << 1);
            diagonal = (motionX ^ motionY) >> 31;
            filtered = this._LoopFilteredWindow(plane, reference, width, height, rawX, rawY, x, y, sourceX, sourceY, window);
          }

          if (!filtered)
            _Window(reference, width, height, sourceX, sourceY, window);
          Vp4Dsp.Predict(window, 0, 9, half, diagonal, output, first, width);
        }

        // vp3_dequant
        var inter = mode == _MODE_INTRA ? 0 : 1;
        var coefficients = this._coefficients[fragment]!;
        var end = this._lastCoefficient[fragment];
        for (var i = 1; i < 64; ++i) {
          int c = coefficients[i];
          if (c != 0) {
            var position = _Transpose(Mpeg4Quantisation.ZigZag[i]);
            block[position] = unchecked((short)(c * this._dequant[inter, plane, position]));
          }
        }

        block[0] = unchecked((short)(this._dc[fragment] * this._dequant[inter, plane, 0]));

        if (mode == _MODE_INTRA)
          Vp4Dsp.Idct(output, first, width, block, add: false);
        else if (end != 0)
          Vp4Dsp.Idct(output, first, width, block, add: true);
        else
          Vp4Dsp.IdctDcAdd(output, first, width, block);
      }
  }

  /// <summary>A 9x9 window of a reference plane, its edges repeated: FFmpeg's <c>emulated_edge_mc</c>.</summary>
  private static void _Window(byte[] plane, int width, int height, int left, int top, byte[] window) {
    for (var r = 0; r < 9; ++r) {
      var row = Math.Clamp(top + r, 0, height - 1) * width;
      for (var c = 0; c < 9; ++c)
        window[r * 9 + c] = plane[row + Math.Clamp(left + c, 0, width - 1)];
    }
  }

  /// <summary>
  /// <c>vp4_mc_loop_filter</c>: where the prediction straddles a block edge of the reference, a
  /// filtered copy of it, or <c>false</c> where nothing needs filtering.
  /// </summary>
  private bool _LoopFilteredWindow(
    int plane, byte[] reference, int width, int height, int motionX, int motionY, int bx, int by,
    int sourceX, int sourceY, byte[] window) {
    var shift = plane != 0 ? 4 : 2;
    var mask = plane != 0 ? 3 : 1;
    var blockWidth = plane != 0 ? 8 : 16;

    var x = 8 * bx + motionX / shift;
    var y = 8 * by + motionY / shift;
    var subX = motionX & mask;
    var subY = motionY & mask;

    Span<byte> loop = stackalloc byte[12 * 12];
    if (subX != 0 || subY != 0) {
      --x;
      --y;
      if (subX != 0)
        x = Math.Min(x, x + Math.Sign(motionX));
      if (subY != 0)
        y = Math.Min(y, y + Math.Sign(motionY));

      var x2 = x + blockWidth;
      var y2 = y + blockWidth;
      if (x2 < 0 || x2 >= width || y2 < 0 || y2 >= height)
        return false;

      var offsetX = (-(x + 2) & 7) + 2;
      var offsetY = (-(y + 2) & 7) + 2;
      var buffer = _Loop(reference, width, height, sourceX - 1, sourceY - 1);
      if (offsetX <= 8 + subX)
        Vp4Dsp.HorizontalLoopFilter(buffer, offsetX, 12, this._bounding, 12);
      if (offsetY <= 8 + subY)
        Vp4Dsp.VerticalLoopFilter(buffer, offsetY * 12, 12, this._bounding, 12);
      _CopyOut(buffer, window);
      return true;
    } else {
      var offsetX = -x & 7;
      var offsetY = -y & 7;
      if (offsetX == 0 && offsetY == 0)
        return false;

      var buffer = _Loop(reference, width, height, sourceX - 1, sourceY - 1);
      if (offsetX != 0)
        Vp4Dsp.HorizontalLoopFilter(buffer, 12 + offsetX + 1, 12, this._bounding, 8);
      if (offsetY != 0)
        Vp4Dsp.VerticalLoopFilter(buffer, (offsetY + 1) * 12 + 1, 12, this._bounding, 8);
      _CopyOut(buffer, window);
      return true;
    }
  }

  private static byte[] _Loop(byte[] plane, int width, int height, int left, int top) {
    var buffer = new byte[12 * 12];
    for (var r = 0; r < 12; ++r) {
      var row = Math.Clamp(top + r, 0, height - 1) * width;
      for (var c = 0; c < 12; ++c)
        buffer[r * 12 + c] = plane[row + Math.Clamp(left + c, 0, width - 1)];
    }

    return buffer;
  }

  private static void _CopyOut(byte[] buffer, byte[] window) {
    for (var r = 0; r < 9; ++r)
      Array.Copy(buffer, (r + 1) * 12 + 1, window, r * 9, 9);
  }

  // ============================================================================================
  // Tables
  // ============================================================================================

  private static MsMpeg4VlcTable _Pattern(int table) {
    var codes = new int[14];
    var lengths = new int[14];
    for (var i = 0; i < 14; ++i) {
      codes[i] = Vp4Tables.BlockPatternVlc[(table * 14 + i) * 2];
      lengths[i] = Vp4Tables.BlockPatternVlc[(table * 14 + i) * 2 + 1];
    }

    return MsMpeg4VlcTable.FromCodes("a VP4 block pattern table", codes, lengths);
  }

  private static MsMpeg4VlcTable[] _BuildVectors() {
    var tables = new MsMpeg4VlcTable[14];
    for (var t = 0; t < 14; ++t) {
      var lengths = new int[63];
      var values = new int[63];
      for (var i = 0; i < 63; ++i) {
        values[i] = Vp4Tables.MvVlc[(t * 63 + i) * 2];
        lengths[i] = Vp4Tables.MvVlc[(t * 63 + i) * 2 + 1];
      }

      tables[t] = MsMpeg4VlcTable.FromLengths("a VP4 motion vector table", lengths, values);
    }

    return tables;
  }

  private static MsMpeg4VlcTable[] _BuildCoefficients() {
    var tables = new MsMpeg4VlcTable[80];
    for (var t = 0; t < 80; ++t) {
      var lengths = new int[32];
      var values = new int[32];
      for (var i = 0; i < 32; ++i) {
        values[i] = Vp4Tables.Bias[(t * 32 + i) * 2];
        lengths[i] = Vp4Tables.Bias[(t * 32 + i) * 2 + 1];
      }

      tables[t] = MsMpeg4VlcTable.FromLengths("a VP4 coefficient table", lengths, values);
    }

    return tables;
  }
}
