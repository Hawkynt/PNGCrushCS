using System;
using System.IO;

namespace FileFormat.Eri;

/// <summary>Decodes the frames of a lossy ERI picture or MEI film (ERI version 28H, the ERISA library's lossy format).</summary>
/// <remarks>
/// The picture is held as Y, U, V (and opacity) in 16×16 macroblocks of four 8×8 blocks per
/// channel — or one per chroma channel when the chroma is sampled 4:1:1. Each block is coded as
/// quantised coefficients of an 8×8 DCT or of the lapped transform (LOT), which overlaps every block
/// with its neighbours and so runs one macroblock behind. Coefficients are scaled per macroblock by
/// a code whose low bit picks 1 or 1.5 and whose upper bits are a power of two, sent in zigzag order,
/// interleaved coefficient by coefficient across the macroblock's blocks, and coded with the order-0
/// ERINA code; the two quantisation tables use an order-0 Huffman code of their own and the scale
/// codes the run-length gamma code. Within a channel the DC of the second to fourth DCT block is
/// sent as its difference from the first's.
/// <para/>
/// A difference frame first predicts every 16×16 block from the frame before it, the frame after it
/// (a bidirectional frame) or both, moved by a vector in half pixels, and adds twice the decoded
/// difference. A frame may ask for a 4-2-1 smoothing filter over the whole picture (shown, but not
/// referred to) or over its intra blocks only (in place).
/// <para/>
/// The arithmetic is the ERISA library's own: the 16-bit fixed-point inverse transforms, colour
/// conversion and motion compensation its shipped x86 code runs on processors with SSE, every
/// rounding, saturation and quirk included, so that what is decoded here is what Entis' players
/// showed. Frames are BGRA, four bytes a pixel, top row first.
/// </remarks>
internal sealed class EriLossyDecoder {

  private const int _AREA = 64;
  private const int _MACRO = 16;
  private const uint _SAMPLING_444 = 0x00040404;
  private const uint _SAMPLING_411 = 0x00040101;

  // ERISA's 8-point inverse DCT in 14-bit fixed point, one row per output sample.
  private static readonly short[] _Idct = [
    16385, 16069, 15137, 13623, 11585, 9103, 6271, 3197,
    16385, 13623, 6271, -3196, -11585, -16069, -15137, -9102,
    16385, 9103, -6270, -16069, -11585, 3197, 15137, 13623,
    16385, 3197, -15137, -9102, 11585, 13623, -6270, -16069,
    16385, -3196, -15137, 9103, 11585, -13623, -6270, 16069,
    16385, -9102, -6270, 16069, -11585, -3196, 15137, -13623,
    16385, -13623, 6271, 3197, -11585, 16069, -15137, 9103,
    16385, -16069, 15137, -13623, 11585, -9102, 6271, -3196,
  ];

  // The LOT's three plane rotations in 15-bit fixed point, as the library's vertical and horizontal
  // passes state them: cos·sin pairs for (1,3), (3,5) and (5,7). The horizontal pass multiplies the
  // odd rows' (3,5) pair by 2908 where every other place has 29081; that is the library's table.
  private const int _ROTATE_1_SIN = 24069, _ROTATE_1_COS = 22237, _ROTATE_1_NEGATIVE_SIN = -24068;
  private const int _ROTATE_3_SIN = 29081, _ROTATE_3_SIN_ODD_ROWS = 2908, _ROTATE_3_COS = 15103, _ROTATE_3_NEGATIVE_SIN = -29080;
  private const int _ROTATE_5_SIN = 31795, _ROTATE_5_COS = 7931, _ROTATE_5_NEGATIVE_SIN = -31794;

  private static readonly int[] _ZigZag = _MakeZigZag();

  private readonly int _width;
  private readonly int _rows;
  private readonly int _channels;
  private readonly bool _fourOneOne;
  private readonly int _blocksets;
  private readonly uint _transformation;
  private readonly int _divWidth;
  private readonly int _divHeight;
  private readonly bool _reverse;
  private readonly int _blocking;
  private readonly uint _architecture;

  // Decoder state the library keeps from frame to frame.
  private readonly short[][] _blocks = new short[16][];
  private readonly byte[] _converted = new byte[_AREA * 16];
  private readonly sbyte[] _vectorFlags;
  private readonly sbyte[] _vectors;
  private readonly _Prediction[] _predictions;
  private readonly short[] _verticalOverlap;
  private readonly short[] _horizontalOverlap = new short[_AREA * 8];

  private struct _Prediction {
    public bool HasPrevious;
    public bool HasNext;
    public int Previous;
    public int Next;
    public int PreviousHalf;
    public int NextHalf;
  }

  /// <summary>A decoded frame: BGRA pixels top row first, and the copy to show when it was smoothed.</summary>
  public readonly record struct Frame(byte[] Pixels, byte[] Shown);

  public EriLossyDecoder(in EriInfoHeader info, int channels) {
    this._blocking = info.BlockingDegree;
    this._architecture = info.Architecture;
    if (channels == 1)
      throw new NotSupportedException("ERI lossy grey pictures are not read: the Entis library's shipped decoder cannot read them (it calls an output routine it never set), so no decode can be verified.");

    this._fourOneOne = info.SamplingFlags switch {
      _SAMPLING_444 => false,
      _SAMPLING_411 => true,
      _ => throw new NotSupportedException($"ERI lossy chroma sampling {info.SamplingFlags:X8} is not read (4:2:2 is withdrawn by the specification)."),
    };

    this._width = info.Width;
    this._rows = info.Rows;
    this._channels = channels;
    this._transformation = info.Transformation;
    this._blocksets = this._fourOneOne ? (channels == 3 ? 6 : 10) : channels * 4;
    this._divWidth = (this._width + _MACRO - 1) / _MACRO;
    this._divHeight = (this._rows + _MACRO - 1) / _MACRO;

    // A picture stored bottom row first is decoded into the frame from its last row up.
    this._reverse = !info.TopDown;

    for (var i = 0; i < this._blocks.Length; ++i)
      this._blocks[i] = new short[_AREA];

    var (widthBlocks, heightBlocks) = this._SizeInBlocks(this._transformation);
    var totalBlocks = widthBlocks * heightBlocks;
    this._vectorFlags = new sbyte[totalBlocks];
    this._vectors = new sbyte[totalBlocks * 4];
    this._predictions = new _Prediction[totalBlocks];
    this._verticalOverlap = new short[widthBlocks * _AREA * 8];
  }

  private (int Width, int Height) _SizeInBlocks(uint transformation) {
    var extra = transformation == EriInfoHeader.LotTransformation ? 1 : 0;
    return (((this._width + 15) >> 4) + extra, ((this._rows + 15) >> 4) + extra);
  }

  private static int[] _MakeZigZag() {
    var table = new int[_AREA];
    var i = 0;
    for (var sum = 0; sum < 15; ++sum)
      for (var step = 0; step <= sum; ++step) {
        var x = (sum & 1) == 0 ? step : sum - step;
        var y = sum - x;
        if (x < 8 && y < 8)
          table[i++] = y * 8 + x;
      }
    return table;
  }

  /// <summary>Whether a frame record codes a bidirectional frame, which refers to the frame after it too.</summary>
  public static bool IsBidirectional(byte[] record) => record.Length > 1 && (record[1] & 0x02) != 0;

  /// <summary>Decodes one frame record.</summary>
  /// <param name="record">The image record.</param>
  /// <param name="previous">The frame a difference frame is predicted from; <c>null</c> for a key frame.</param>
  /// <param name="next">For a bidirectional frame, the frame after it.</param>
  public Frame Decode(byte[] record, byte[]? previous, byte[]? next) {
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
    if (this._blocking != 3)
      throw new InvalidDataException("ERI lossy picture must be blocked 8×8.");
    if (this._architecture != EriInfoHeader.RunLengthGamma)
      throw new InvalidDataException("ERI lossy picture must name the run-length gamma architecture.");

    var difference = previous is not null;
    if ((operations & 0x02) != 0 && next is null)
      throw new InvalidDataException("ERI bidirectional frame has no frame after it to refer to.");

    // A frame may ask for the DCT whatever the header says.
    var transformation = encodeType == 1 ? EriInfoHeader.DctTransformation : this._transformation;
    var lot = transformation == EriInfoHeader.LotTransformation;
    var (widthBlocks, heightBlocks) = this._SizeInBlocks(transformation);
    var totalBlocks = widthBlocks * heightBlocks;
    if (totalBlocks > this._vectorFlags.Length)
      throw new InvalidDataException("ERI lossy frame asks for a transform its picture header does not allow.");

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
    var scales = new sbyte[totalBlocks * 2];
    gamma.Decode(scales);

    var divBlocks = this._divWidth * this._divHeight;
    if ((operations & 0x01) != 0) {
      gamma.Reset();
      gamma.Decode(this._vectorFlags.AsSpan(0, divBlocks));
      gamma.Reset();
      gamma.Decode(this._vectors.AsSpan(0, divBlocks * 4));
    } else if (difference) {
      this._vectorFlags.AsSpan(0, divBlocks).Fill(1);
      this._vectors.AsSpan(0, divBlocks * 4).Clear();
    }

    var frame = new byte[this._width * this._rows * 4];
    if (difference)
      this._SetUpPrediction(previous!, (operations & 0x02) != 0 ? next : null);

    if (lot)
      Array.Clear(this._verticalOverlap);

    var levels = new sbyte[widthBlocks * _AREA * this._blocksets];
    var ordered = new sbyte[_AREA * this._blocksets];
    var line = new byte[widthBlocks * _MACRO * _MACRO * 4];
    var rowsDone = 0;
    var predictionRow = 0;

    for (var by = 0; by < heightBlocks; ++by) {
      if (lot)
        Array.Clear(this._horizontalOverlap);

      coefficients.Decode(levels);
      var stored = 0;
      for (var bx = 0; bx < widthBlocks; ++bx) {
        this._Dequantise(levels.AsSpan(bx * _AREA * this._blocksets, _AREA * this._blocksets), ordered,
          scales[(by * widthBlocks + bx) * 2], scales[(by * widthBlocks + bx) * 2 + 1], quantiser, lot);

        if (lot)
          this._InverseLot(bx);
        else
          for (var b = 0; b < this._blocksets; ++b)
            _InverseDct(this._blocks[b], 14, 16);

        // The LOT's output runs one macroblock behind, across and down.
        var x = lot ? bx - 1 : bx;
        var y = lot ? by - 1 : by;
        if (x < 0 || y < 0)
          continue;

        this._Scale(difference, line.AsSpan(stored * _MACRO * _MACRO * 4, _MACRO * _MACRO * 4));
        ++stored;
      }

      if (lot && by == 0)
        continue;

      var height = Math.Min(this._rows - (lot ? by - 1 : by) * _MACRO, _MACRO);
      if (difference)
        this._RestoreDifference(frame, line, rowsDone, height, predictionRow, previous!, next);
      else
        this._Restore(frame, line, rowsDone, height);

      rowsDone += _MACRO;
      predictionRow += widthBlocks;
    }

    var shown = frame;
    if ((operations & 0x04) != 0) {
      shown = (byte[])frame.Clone();
      this._FilterWhole(frame, shown);
    } else if ((operations & 0x08) != 0)
      this._FilterIntraBlocks(frame);

    return new(frame, shown);
  }

  // ------------------------------------------------------------------ dequantisation

  private void _Dequantise(ReadOnlySpan<sbyte> sent, sbyte[] ordered, sbyte lumaScale, sbyte chromaScale, byte[] table, bool lot) {
    var count = this._blocksets;

    // The coefficients come interleaved: every block's first coefficient, then every block's second.
    for (var b = 0; b < count; ++b)
      for (var j = 0; j < _AREA; ++j)
        ordered[b * _AREA + j] = sent[j * count + b];

    if (!lot) {
      _UndoDcDifference(ordered, 0);
      if (!this._fourOneOne)
        for (var c = 1; c < this._channels; ++c)
          _UndoDcDifference(ordered, c * 4 * _AREA);
      else if (this._channels == 4)
        _UndoDcDifference(ordered, 6 * _AREA);
    }

    Span<ushort> luma = stackalloc ushort[_AREA];
    Span<ushort> chroma = stackalloc ushort[_AREA];
    Span<int> shift = stackalloc int[2];

    // The library's MMX routine for 4:1:1 colour without opacity keeps the scale's exponent apart.
    var split = count == 6;
    _Parameters(lumaScale, table.AsSpan(0, _AREA), luma, split, out shift[0]);
    _Parameters(chromaScale, table.AsSpan(_AREA, _AREA), chroma, split, out shift[1]);

    for (var b = 0; b < count; ++b) {
      var isChroma = this._fourOneOne ? b is 4 or 5 : b is >= 4 and < 12;
      var parameter = isChroma ? chroma : luma;
      var right = split ? shift[isChroma ? 1 : 0] : 3;
      var block = this._blocks[b];
      for (var j = 0; j < _AREA; ++j) {
        var product = unchecked((short)(ordered[b * _AREA + j] * parameter[j]));
        block[_ZigZag[j]] = (short)(product >> Math.Min(right, 15));
      }
    }
  }

  private static void _UndoDcDifference(sbyte[] ordered, int first) {
    for (var b = 1; b < 4; ++b)
      ordered[first + b * _AREA] = unchecked((sbyte)(ordered[first + b * _AREA] + ordered[first]));
  }

  /// <summary>One table's sixteen-bit step sizes for a scale code.</summary>
  /// <param name="split">
  /// The 4:1:1 colour routine: step = (q+1)·2^e, tripled for a half step, and the product shifted
  /// right by -e (plus one for a half step) when e is negative. Otherwise step = (q+1)·2^(e+3)·1.5
  /// rounded down (or shifted right when e+3 is negative), and the product shifted right by three.
  /// </param>
  private static void _Parameters(sbyte scale, ReadOnlySpan<byte> table, Span<ushort> parameters, bool split, out int productShift) {
    var half = scale & 1;
    var exponent = scale >> 1;
    if (split) {
      var left = exponent < 0 ? 0 : exponent;
      productShift = exponent < 0 ? -exponent + half : half;
      for (var j = 0; j < _AREA; ++j) {
        var step = left > 15 ? 0 : (ushort)((table[j] + 1) << left);
        parameters[j] = half != 0 ? unchecked((ushort)(step * 3)) : (ushort)step;
      }
      return;
    }

    productShift = 3;
    var mask = -half;
    var count = exponent + 3;
    for (var j = 0; j < _AREA; ++j) {
      var step = table[j] + 1;
      if (count < 0)
        step = (step + ((step >> 1) & mask)) >> (-count & 31);
      else {
        step <<= count & 31;
        step += (step >> 1) & mask;
      }
      parameters[j] = unchecked((ushort)step);
    }
  }

  // ------------------------------------------------------------------ inverse transforms

  private static short _Saturate(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

  /// <summary>The library's MMX 8×8 inverse DCT: each pass a 32-bit dot product, shifted and saturated to 16 bits.</summary>
  private static void _InverseDct(short[] block, int firstShift, int secondShift) {
    Span<short> temp = stackalloc short[_AREA];
    // Along each row of coefficients; the results land transposed.
    for (var n = 0; n < 8; ++n)
      for (var row = 0; row < 8; ++row) {
        var sum = 0;
        for (var k = 0; k < 8; ++k)
          sum = unchecked(sum + block[row * 8 + k] * _Idct[n * 8 + k]);
        temp[n * 8 + row] = _Saturate(sum >> firstShift);
      }

    for (var n = 0; n < 8; ++n)
      for (var row = 0; row < 8; ++row) {
        var sum = 0;
        for (var k = 0; k < 8; ++k)
          sum = unchecked(sum + temp[row * 8 + k] * _Idct[n * 8 + k]);
        block[n * 8 + row] = _Saturate(sum >> secondShift);
      }
  }

  private static int _Pair(int a, int ca, int b, int cb) => unchecked(a * ca + b * cb);

  // A 32-bit product doubled with wrap-around and its upper half taken, saturated to 16 bits.
  private static short _UpperDoubled(int value) => _Saturate(unchecked((int)((uint)value << 1)) >> 16);

  /// <summary>The inverse LOT of every block of a macroblock, with the overlap buffers the library threads between them.</summary>
  private void _InverseLot(int bx) {
    var column = bx * _AREA * 8;
    var horizontal = 0;
    var vertical = column;
    var b = 0;

    // Luminance: blocks across share the horizontal buffer of their row, blocks down the vertical one of their column.
    for (var i = 0; i < 2; ++i, horizontal += _AREA)
      for (var j = 0; j < 2; ++j)
        this._InverseLotBlock(this._blocks[b++], horizontal, vertical + j * _AREA);
    vertical += _AREA * 2;

    if (this._channels >= 3) {
      if (!this._fourOneOne)
        for (var c = 0; c < 2; ++c, vertical += _AREA * 2)
          for (var i = 0; i < 2; ++i, horizontal += _AREA)
            for (var j = 0; j < 2; ++j)
              this._InverseLotBlock(this._blocks[b++], horizontal, vertical + j * _AREA);
      else
        for (var c = 0; c < 2; ++c, horizontal += _AREA, vertical += _AREA)
          this._InverseLotBlock(this._blocks[b++], horizontal, vertical);
    }

    if (this._channels < 4)
      return;

    for (var i = 0; i < 2; ++i, horizontal += _AREA)
      for (var j = 0; j < 2; ++j)
        this._InverseLotBlock(this._blocks[b++], horizontal, vertical + j * _AREA);
  }

  private void _InverseLotBlock(short[] d, int horizontal, int vertical) {
    var hb = this._horizontalOverlap;
    var vb = this._verticalOverlap;

    // Down each column: rotate rows (5,7), (3,5) and (1,3), carrying the scaled sums.
    for (var c = 0; c < 8; ++c) {
      int r1 = d[8 + c], r3 = d[24 + c], r5 = d[40 + c], r7 = d[56 + c];
      d[56 + c] = _Saturate(_Pair(r5, _ROTATE_5_NEGATIVE_SIN, r7, _ROTATE_5_COS) >> 13);
      var t = _Saturate(_Pair(r5, _ROTATE_5_COS, r7, _ROTATE_5_SIN) >> 11);
      var a3 = unchecked((short)(r3 << 4));
      d[40 + c] = _Saturate(_Pair(a3, _ROTATE_3_NEGATIVE_SIN, t, _ROTATE_3_COS) >> 17);
      var t2 = _Saturate(_Pair(a3, _ROTATE_3_COS, t, _ROTATE_3_SIN) >> 15);
      var a1 = unchecked((short)(r1 << 4));
      d[24 + c] = _Saturate(_Pair(a1, _ROTATE_1_NEGATIVE_SIN, t2, _ROTATE_1_COS) >> 17);
      d[8 + c] = _Saturate(_Pair(a1, _ROTATE_1_COS, t2, _ROTATE_1_SIN) >> 17);
    }

    // Undo the vertical overlap against the block above.
    for (var p = 0; p < 4; ++p)
      for (var c = 0; c < 8; ++c) {
        var even = (2 * p) * 8 + c;
        var odd = even + 8;
        var a = unchecked((short)(d[even] << 2));
        var sum = _Saturate(a + d[odd]);
        var diff = _Saturate(a - d[odd]);
        var old = vb[vertical + even];
        vb[vertical + even] = sum;
        vb[vertical + odd] = diff;
        d[even] = _Saturate(old + diff);
        d[odd] = _Saturate(old - diff);
      }

    // Along each row: rotate columns (5,7), (3,5) and (1,3).
    for (var r = 0; r < 8; ++r) {
      var o = r * 8;
      int x1 = d[o + 1], x3 = d[o + 3], x5 = d[o + 5], x7 = d[o + 7];
      d[o + 7] = _Saturate(_Pair(x5, _ROTATE_5_NEGATIVE_SIN, x7, _ROTATE_5_COS) >> 15);
      var t = _UpperDoubled(_Pair(x5, _ROTATE_5_COS, x7, _ROTATE_5_SIN));
      d[o + 5] = _Saturate(_Pair(x3, _ROTATE_3_NEGATIVE_SIN, t, _ROTATE_3_COS) >> 15);
      var t2 = _UpperDoubled(_Pair(x3, _ROTATE_3_COS, t, (r & 1) == 0 ? _ROTATE_3_SIN : _ROTATE_3_SIN_ODD_ROWS));
      d[o + 1] = _Saturate(_Pair(x1, _ROTATE_1_COS, t2, _ROTATE_1_SIN) >> 15);
      d[o + 3] = _Saturate(_Pair(x1, _ROTATE_1_NEGATIVE_SIN, t2, _ROTATE_1_COS) >> 15);
    }

    // Undo the horizontal overlap against the block to the left.
    for (var r = 0; r < 8; ++r)
      for (var m = 0; m < 4; ++m) {
        var even = r * 8 + 2 * m;
        var sum = _Saturate(d[even] + d[even + 1]);
        var diff = _Saturate(d[even] - d[even + 1]);
        var old = hb[horizontal + even];
        hb[horizontal + even] = sum;
        hb[horizontal + even + 1] = diff;
        d[even] = (short)(_Saturate(old + diff) >> 1);
        d[even + 1] = (short)(_Saturate(old - diff) >> 1);
      }

    _InverseDct(d, 16, 17);
  }

  // ------------------------------------------------------------------ macroblock assembly

  private void _Convert(int block, bool signed) {
    var source = this._blocks[block];
    var target = this._converted.AsSpan(block * _AREA, _AREA);
    for (var i = 0; i < _AREA; ++i)
      target[i] = signed ? (byte)(sbyte)Math.Clamp((int)source[i], -128, 127) : (byte)Math.Clamp((int)source[i], 0, 255);
  }

  /// <summary>
  /// Lays a macroblock out as the library's colour conversion reads it: per row, four groups of
  /// four pixels, each group four luminance, four U, four V and four opacity bytes.
  /// </summary>
  private void _Scale(bool difference, Span<byte> macroblock) {
    var b = this._converted;
    for (var i = 0; i < 4; ++i)
      this._Convert(i, difference);

    if (!this._fourOneOne) {
      for (var i = 4; i < 12; ++i)
        this._Convert(i, true);
      if (this._channels >= 4)
        for (var i = 12; i < 16; ++i)
          this._Convert(i, difference);

      for (var y = 0; y < _MACRO; ++y)
        for (var x = 0; x < _MACRO; ++x) {
          var at = (y >> 3) * 2 + (x >> 3);
          var within = (y & 7) * 8 + (x & 7);
          var o = y * 64 + (x >> 2) * 16 + (x & 3);
          macroblock[o] = b[at * _AREA + within];
          macroblock[o + 4] = b[(4 + at) * _AREA + within];
          macroblock[o + 8] = b[(8 + at) * _AREA + within];
          macroblock[o + 12] = b[(12 + at) * _AREA + within];
        }
      return;
    }

    for (var i = 4; i < 6; ++i)
      this._Convert(i, true);
    if (this._channels >= 4)
      for (var i = 6; i < 10; ++i)
        this._Convert(i, difference);

    Span<byte> u = stackalloc byte[_MACRO];
    Span<byte> v = stackalloc byte[_MACRO];
    Span<int> uv = stackalloc int[8];
    Span<int> vv = stackalloc int[8];
    for (var y = 0; y < _MACRO; ++y) {
      // Chroma is biased to unsigned and averaged rounding up: down between this row and the next
      // on odd rows (not past the last), then across with the sample to the left of each.
      var row = y >> 1;
      var below = (y & 1) != 0 && y != 15 ? row + 1 : row;
      for (var k = 0; k < 8; ++k) {
        uv[k] = _Average(b[4 * _AREA + row * 8 + k] ^ 0x80, b[4 * _AREA + below * 8 + k] ^ 0x80);
        vv[k] = _Average(b[5 * _AREA + row * 8 + k] ^ 0x80, b[5 * _AREA + below * 8 + k] ^ 0x80);
      }
      for (var k = 0; k < 8; ++k) {
        var left = Math.Max(k - 1, 0);
        u[2 * k] = (byte)(uv[k] ^ 0x80);
        u[2 * k + 1] = (byte)(_Average(uv[left], uv[k]) ^ 0x80);
        v[2 * k] = (byte)(vv[k] ^ 0x80);
        v[2 * k + 1] = (byte)(_Average(vv[left], vv[k]) ^ 0x80);
      }

      for (var x = 0; x < _MACRO; ++x) {
        var at = (y >> 3) * 2 + (x >> 3);
        var within = (y & 7) * 8 + (x & 7);
        var o = y * 64 + (x >> 2) * 16 + (x & 3);
        macroblock[o] = b[at * _AREA + within];
        macroblock[o + 4] = u[x];
        macroblock[o + 8] = v[x];
        macroblock[o + 12] = b[(6 + at) * _AREA + within];
      }
    }
  }

  private static int _Average(int a, int b) => (a + b + 1) >> 1;

  private static int _AverageDown(int a, int b) => (a + b) >> 1;

  // ------------------------------------------------------------------ colour conversion and output

  private int _LineStart(int row) => (this._reverse ? this._rows - 1 - row : row) * this._width * 4;

  private int _LineBytes => this._reverse ? -this._width * 4 : this._width * 4;

  private static (int B, int G, int R) _ToRgb(int y, int u, int v)
    => (y + ((u * 7) >> 2), y - ((u * 3 + v * 6) >> 3), y + ((v * 3) >> 1));

  private static byte _Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

  private void _Restore(byte[] frame, byte[] line, int firstRow, int height) {
    for (var y = 0; y < height; ++y) {
      var target = this._LineStart(firstRow + y);
      for (var x = 0; x < this._width; ++x) {
        var o = (x >> 4) * _MACRO * _MACRO * 4 + y * 64 + ((x & 15) >> 2) * 16 + (x & 3);
        var (b, g, r) = _ToRgb(line[o], (sbyte)line[o + 4], (sbyte)line[o + 8]);
        var p = target + x * 4;
        frame[p] = _Clamp(b);
        frame[p + 1] = _Clamp(g);
        frame[p + 2] = _Clamp(r);
        frame[p + 3] = line[o + 12];
      }
    }
  }

  private void _RestoreDifference(byte[] frame, byte[] line, int firstRow, int height, int predictionRow,
    byte[] previous, byte[]? next) {
    var groups = this._width & ~3;
    for (var y = 0; y < height; ++y) {
      var target = this._LineStart(firstRow + y);
      this._PredictLine(frame, target, predictionRow, previous, next);

      for (var x = 0; x < this._width; ++x) {
        var o = (x >> 4) * _MACRO * _MACRO * 4 + y * 64 + ((x & 15) >> 2) * 16 + (x & 3);
        var p = target + x * 4;
        int db, dg, dr, da;
        if (x < groups) {
          // Four pixels at a time: signed differences, doubled, added with saturation.
          (db, dg, dr) = _ToRgb((sbyte)line[o], (sbyte)line[o + 4], (sbyte)line[o + 8]);
          da = (sbyte)line[o + 12];
        } else {
          // The last pixels of a row go through the library's plain routine, which reads the
          // luminance difference as unsigned, and only then are taken as signed differences.
          var (b, g, r) = _ToRgb(line[o], (sbyte)line[o + 4], (sbyte)line[o + 8]);
          db = (sbyte)_Clamp(b);
          dg = (sbyte)_Clamp(g);
          dr = (sbyte)_Clamp(r);
          da = (sbyte)line[o + 12];
        }

        frame[p] = _Clamp(frame[p] + 2 * db);
        frame[p + 1] = _Clamp(frame[p + 1] + 2 * dg);
        frame[p + 2] = _Clamp(frame[p + 2] + 2 * dr);
        frame[p + 3] = _Clamp(frame[p + 3] + 2 * da);
      }
    }
  }

  // ------------------------------------------------------------------ motion compensation

  private void _SetUpPrediction(byte[] previous, byte[]? next) {
    var w = this._divWidth;
    var h = this._divHeight;
    var vectors = this._vectors;

    // Vectors are sent as differences from the block above, then from the block to the left.
    for (var i = w * 4; i < w * h * 4; ++i)
      vectors[i] = unchecked((sbyte)(vectors[i] + vectors[i - w * 4]));
    for (var y = 0; y < h; ++y)
      for (var x = 1; x < w; ++x)
        for (var k = 0; k < 4; ++k) {
          var i = (y * w + x) * 4 + k;
          vectors[i] = unchecked((sbyte)(vectors[i] + vectors[i - 4]));
        }

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var block = y * w + x;
        var type = this._vectorFlags[block];
        ref var prediction = ref this._predictions[block];
        var v = block * 4;
        prediction.HasPrevious = type is 1 or 2;
        prediction.HasNext = next is not null && type is -1 or 2;
        if (prediction.HasPrevious)
          prediction.Previous = this._Reference(x, y, vectors[v], vectors[v + 1]);
        if (prediction.HasNext)
          prediction.Next = this._Reference(x, y, vectors[v + 2], vectors[v + 3]);
        prediction.PreviousHalf = (vectors[v] & 1) | ((vectors[v + 1] & 1) << 1);
        prediction.NextHalf = (vectors[v + 2] & 1) | ((vectors[v + 3] & 1) << 1);
      }
  }

  private int _Reference(int x, int y, sbyte dx, sbyte dy) {
    var rx = x * _MACRO + (dx >> 1);
    var ry = y * _MACRO + (dy >> 1);
    // The library checks the bottom edge against the width, as it does the right.
    if ((rx | ry) < 0 || ry + _MACRO > this._width || rx + _MACRO > this._width) {
      rx = x * _MACRO;
      ry = y * _MACRO;
    }
    return this._LineStart(0) + rx * 4 + ry * this._LineBytes;
  }

  private static byte _At(byte[] image, int index)
    => (uint)index < (uint)image.Length ? image[index] : throw new InvalidDataException("ERI motion vector points outside the frame it refers to.");

  /// <summary>Writes one line's prediction into the frame, advancing every block's reference by a line.</summary>
  private void _PredictLine(byte[] frame, int target, int predictionRow, byte[] previous, byte[]? next) {
    var lineBytes = this._LineBytes;
    var full = this._width >> 4;
    var rest = this._width & 15;
    for (var c = 0; c < full + (rest != 0 ? 1 : 0); ++c) {
      var index = predictionRow + c;
      if (index >= this._predictions.Length)
        throw new InvalidDataException("ERI difference frame runs past its block table.");
      ref var prediction = ref this._predictions[index];
      var o = target + c * 64;
      var count = c < full ? 64 : rest * 4;

      var (fromPrevious, fromNext) = (prediction.HasPrevious, prediction.HasNext);
      if (!fromPrevious && !fromNext) {
        frame.AsSpan(o, count).Clear();
        continue;
      }

      if (c == full) {
        // A partial block at the right edge: whole pixels only, and both references averaged down.
        for (var i = 0; i < count; ++i)
          frame[o + i] = fromPrevious && fromNext
            ? (byte)((_At(previous, prediction.Previous + i) >> 1) + (_At(next!, prediction.Next + i) >> 1))
            : fromPrevious ? _At(previous, prediction.Previous + i) : _At(next!, prediction.Next + i);
      } else {
        if (fromPrevious)
          _PredictBlock(frame, o, previous, prediction.Previous, prediction.PreviousHalf, lineBytes, blend: false);
        if (fromNext)
          _PredictBlock(frame, o, next!, prediction.Next, prediction.NextHalf, lineBytes, blend: fromPrevious);
      }

      if (fromPrevious)
        prediction.Previous += lineBytes;
      if (fromNext)
        prediction.Next += lineBytes;
    }
  }

  /// <summary>
  /// One 16-pixel line of a block from a reference, at whole or half pixels, rounding up; when
  /// <paramref name="blend"/>, averaged with what is already there.
  /// </summary>
  private static void _PredictBlock(byte[] frame, int o, byte[] image, int at, int half, int lineBytes, bool blend) {
    for (var i = 0; i < 64; ++i) {
      int value = _At(image, at + i);
      switch (half) {
        case 1:
          value = _Average(value, _At(image, at + i + 4));
          break;
        case 2:
          value = _Average(value, _At(image, at + lineBytes + i));
          break;
        case 3: {
          var upper = _Average(value, _At(image, at + i + 4));
          // The library takes the last two pixels of each half-line's lower neighbour from the left.
          var right = (i & 31) >= 24 ? i - 4 : i + 4;
          var lower = _Average(_At(image, at + lineBytes + i), _At(image, at + lineBytes + right));
          value = _Average(upper, lower);
          break;
        }
      }
      frame[o + i] = blend ? (byte)_Average(value, frame[o + i]) : (byte)value;
    }
  }

  // ------------------------------------------------------------------ the 4-2-1 loop filter

  private void _FilterWhole(byte[] source, byte[] target) {
    if (this._width < 2 || this._rows < 2)
      return;
    this._FilterRegion(source, target, 0, 0, this._width, this._rows, (this._width & 1) == 0, this._LineBuffers());
  }

  private byte[][] _LineBuffers() => [new byte[this._width * 4], new byte[this._width * 4], new byte[this._width * 4]];

  private void _FilterIntraBlocks(byte[] frame) {
    if (this._width < 2 || this._rows < 2)
      return;

    var rounding = (this._width & 1) == 0;
    var buffers = this._LineBuffers();
    for (var by = 0; by < this._divHeight; ++by) {
      var height = Math.Min(_MACRO, this._rows - by * _MACRO);
      var bx = 0;
      while (bx < this._divWidth) {
        if (this._vectorFlags[by * this._divWidth + bx] != 0) {
          ++bx;
          continue;
        }

        if (height < 2)
          throw new NotSupportedException("ERI frame asks to smooth intra blocks in a last block row one line high, which runs the Entis decoder off the end of its frame.");

        var first = bx++;
        while (bx < this._divWidth && this._vectorFlags[by * this._divWidth + bx] == 0)
          ++bx;
        var width = Math.Min((bx - first) * _MACRO, this._width - first * _MACRO);

        // The library's line filter writes nothing for a strip one pixel wide, which then takes
        // whatever its heap left in the line buffers.
        if (width < 2)
          throw new NotSupportedException("ERI frame smooths a strip of intra blocks one pixel wide, for which the Entis decoder reads line buffers it never wrote, so no decode can be verified.");
        this._FilterRegion(frame, frame, first * _MACRO, by * _MACRO, width, height, rounding, buffers);
      }
    }
  }

  /// <summary>
  /// Smooths a region 2-1-1 across each line, then 2-1-1 down, rounding up (the SSE routine, for even
  /// picture widths) or down (the plain one, for odd widths, which also repeats the second-to-last
  /// pixel into the last). The first and last line are smoothed across only.
  /// </summary>
  private void _FilterRegion(byte[] source, byte[] target, int left, int top, int width, int height, bool roundUp, byte[][] buffers) {
    var lineBytes = this._LineBytes;
    var origin = this._LineStart(0) + left * 4;

    void Across(int row, byte[] into) {
      var at = origin + (top + row) * lineBytes;
      for (var i = 0; i < 4; ++i) {
        into[i] = source[at + i];
        into[(width - 1) * 4 + i] = roundUp ? source[at + (width - 1) * 4 + i] : source[at + (width - 2) * 4 + i];
      }
      for (var x = 1; x < width - 1; ++x)
        for (var i = 0; i < 4; ++i) {
          int l = source[at + (x - 1) * 4 + i], c = source[at + x * 4 + i], r = source[at + (x + 1) * 4 + i];
          into[x * 4 + i] = (byte)(roundUp ? _Average(c, _Average(l, r)) : _AverageDown(_AverageDown(l, r), c));
        }
    }

    void Store(int row, byte[] from) => from.AsSpan(0, width * 4).CopyTo(target.AsSpan(origin + (top + row) * lineBytes));

    Across(0, buffers[0]);
    Store(0, buffers[0]);
    Across(1, buffers[1]);
    for (var row = 2; row < height; ++row) {
      Across(row, buffers[2]);
      var at = origin + (top + row - 1) * lineBytes;
      for (var i = 0; i < width * 4; ++i) {
        int a = buffers[0][i], c = buffers[1][i], b = buffers[2][i];
        target[at + i] = (byte)(roundUp ? _Average(_Average(a, b), c) : _AverageDown(c, _AverageDown(a, b)));
      }
      (buffers[0], buffers[1], buffers[2]) = (buffers[1], buffers[2], buffers[0]);
    }
    Store(height - 1, buffers[1]);
  }
}
