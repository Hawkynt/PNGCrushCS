using System;
using System.Collections.Generic;
using FileFormat.Codecs.MsMpeg4;

namespace FileFormat.Codecs.Svq3;

/// <summary>
/// Writes SVQ3 pictures from 4:2:0 planes: I pictures of flat-predicted intra blocks, and P pictures
/// whose macroblocks are skipped, coded as a residual over the co-located reference block, or coded
/// intra.
/// </summary>
/// <remarks>
/// Every prediction it uses comes from outside the picture being coded: the intra macroblock is
/// type 33, whose luminance and chrominance both predict from a flat 128, and the inter macroblock is
/// a whole-sample 16x16 block whose vector is nought. With every vector in the stream nought the
/// median prediction is nought as well, so a vector difference of nought states it. The reference is
/// what <see cref="Svq3Decoder"/> makes of the packet just written, so the encoder never drifts from
/// any decoder that agrees with that one.
/// </remarks>
internal sealed class Svq3Encoder {

  private const uint _INTRA_RAW_TYPE_I = 25; // 33 once the I picture's offset of eight is added
  private const uint _INTRA_RAW_TYPE_P = 33;
  private const uint _INTER_RAW_TYPE = 1;

  private static readonly int[] _Basis = [13, 13, 13, 13, 17, 7, -7, -17, 13, -13, -13, 13, 7, -17, 17, -7];

  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _qscale;
  private readonly Svq3Decoder _reconstruction;
  private Svq3Frame? _reference;
  private int _pictureNumber;

  internal Svq3Encoder(int width, int height, int qscale) {
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._qscale = qscale;
    this._reconstruction = new(width, height, halfPel: false, thirdPel: false, lowDelay: true, hasWatermark: false, watermarkKey: 0);
  }

  /// <summary>The <c>SEQH</c> atom: explicit size, whole-sample vectors only, no B pictures, no watermark.</summary>
  internal static byte[] SequenceHeader(int width, int height) {
    var bits = new MsMpeg4BitWriter();
    bits.Write(7, 3);
    bits.Write(width, 12);
    bits.Write(height, 12);
    bits.Write(0, 1); // half-sample vectors
    bits.Write(0, 1); // third-sample vectors
    bits.Write(0, 4);
    bits.Write(1, 1); // low delay
    bits.Write(0, 1);
    bits.Write(0, 1); // no extension bytes
    bits.Write(0, 1); // no watermark
    var body = bits.ToArray();
    var atom = new byte[8 + body.Length];
    "SEQH"u8.CopyTo(atom);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(atom.AsSpan(4), (uint)body.Length);
    body.CopyTo(atom, 8);
    return atom;
  }

  /// <summary>One picture; <paramref name="planes"/> is the Y, Cb and Cr of a <c>Yuv420P8</c> image.</summary>
  internal byte[] Encode(ReadOnlySpan<byte> planes, bool key) {
    key |= this._reference == null;
    var slice = new MsMpeg4BitWriter();
    _Ue(slice, key ? 2u : 0u); // slice type: 0 P, 2 I
    slice.Write(0, 1);          // no media key
    slice.Write(this._pictureNumber & 0xFF, 8);
    slice.Write(this._qscale, 5);
    slice.Write(0, 1);          // no adaptive quantisation
    slice.Write(0, 1);
    slice.Write(0, 1);
    slice.Write(0, 2);
    slice.Write(0, 1);          // no extension bytes

    var source = new Source(planes, this._width, this._height);
    for (var my = 0; my < this._macroblockHeight; ++my)
      for (var mx = 0; mx < this._macroblockWidth; ++mx)
        this._Macroblock(slice, source, mx, my, key);

    // A slice shorter than its two-byte tail cannot be stated; it is never that short in practice.
    while (slice.BitCount < 16 || (slice.BitCount & 7) != 0)
      slice.Write(0, 1);

    var data = slice.ToArray();
    var length = data.Length;
    var packet = new byte[4 + length];
    packet[0] = 0x61; // slice header 1 with a three-byte length
    packet[1] = (byte)(length >> 16);
    packet[2] = (byte)(length >> 8);
    packet[3] = (byte)length;
    // The decoder moves the slice's last two bytes over the two length bytes it skips, so the slice's
    // first two bytes travel at its end.
    data.AsSpan(2).CopyTo(packet.AsSpan(4));
    packet[2 + length] = data[0];
    packet[3 + length] = data[1];

    this._reference = this._reconstruction.Decode(packet)
      ?? throw new InvalidOperationException("The SVQ3 reconstruction decoder handed back no picture.");
    ++this._pictureNumber;
    return packet;
  }

  private void _Macroblock(MsMpeg4BitWriter output, Source source, int mx, int my, bool key) {
    var luma = new int[256];
    var cb = new int[64];
    var cr = new int[64];
    source.Block(0, 16 * mx, 16 * my, 16, luma);
    source.Block(1, 8 * mx, 8 * my, 8, cb);
    source.Block(2, 8 * mx, 8 * my, 8, cr);

    var flatLuma = new int[256];
    var flatChroma = new int[64];
    flatLuma.AsSpan().Fill(128);
    flatChroma.AsSpan().Fill(128);
    var best = this._Evaluate(luma, cb, cr, flatLuma, flatChroma, flatChroma, intra: true, key);

    if (!key) {
      var reference = this._reference!;
      var predictionLuma = new int[256];
      var predictionCb = new int[64];
      var predictionCr = new int[64];
      for (var y = 0; y < 16; ++y)
        for (var x = 0; x < 16; ++x)
          predictionLuma[16 * y + x] = reference.Luma[reference.LumaOrigin + (16 * my + y) * reference.LumaStride + 16 * mx + x];
      for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x) {
          var at = reference.ChromaOrigin + (8 * my + y) * reference.ChromaStride + 8 * mx + x;
          predictionCb[8 * y + x] = reference.Cb[at];
          predictionCr[8 * y + x] = reference.Cr[at];
        }

      var inter = this._Evaluate(luma, cb, cr, predictionLuma, predictionCb, predictionCr, intra: false, key);
      if (inter.Cost < best.Cost)
        best = inter;

      // Skipping costs one bit and leaves the prediction as it is.
      var skip = _Sse(luma, predictionLuma) + _Sse(cb, predictionCb) + _Sse(cr, predictionCr) + this._Lambda;
      if (skip <= best.Cost) {
        _Ue(output, 0);
        return;
      }
    }

    var cbp = best.Cbp;
    if (best.Intra) {
      _Ue(output, key ? _INTRA_RAW_TYPE_I : _INTRA_RAW_TYPE_P);
      _Ue(output, (uint)Array.IndexOf(Svq3Tables.GolombToIntra4x4Cbp, (byte)cbp));
    } else {
      _Ue(output, _INTER_RAW_TYPE);
      _Ue(output, 0); // vector difference, vertical
      _Ue(output, 0); // and horizontal
      _Ue(output, (uint)Array.IndexOf(Svq3Tables.GolombToInterCbp, (byte)cbp));
    }

    for (var group = 0; group < 4; ++group)
      if ((cbp & (1 << group)) != 0)
        for (var j = 0; j < 4; ++j)
          _Block(output, best.Luma[4 * group + j], 0, best.LumaType);

    if ((cbp & 0x30) == 0)
      return;

    for (var p = 0; p < 2; ++p)
      _Block(output, best.Dc[p], 0, 3);
    if ((cbp & 0x20) != 0)
      for (var p = 0; p < 2; ++p)
        for (var j = 0; j < 4; ++j)
          _Block(output, best.Ac[p][j], 1, 1);
  }

  /// <summary>One way of coding a macroblock: its levels, and what it costs in distortion and bits.</summary>
  private sealed record Candidate(bool Intra, int LumaType, short[][] Luma, short[][] Dc, short[][][] Ac, int Cbp, double Cost);

  /// <summary>The Lagrangian weight of a bit, in squared sample error: grows with the square of the quantiser step.</summary>
  private double _Lambda {
    get {
      var step = Svq3Tables.DequantCoeff[this._qscale] * 676.0 / (1 << 20);
      return 0.1 * step * step;
    }
  }

  /// <summary>
  /// Quantises a macroblock's residual over one prediction and prices the result: the distortion is
  /// that of the dequantised levels put back through the transform, unrounded, and the rate is the
  /// exact length of the codes.
  /// </summary>
  private Candidate _Evaluate(int[] luma, int[] cb, int[] cr, int[] predictionLuma, int[] predictionCb, int[] predictionCr, bool intra, bool key) {
    var lumaQmul = Svq3Tables.DequantCoeff[this._qscale];
    var chromaQmul = Svq3Tables.DequantCoeff[Svq3Tables.ChromaQp[this._qscale + 12] - 12];
    var rounding = intra ? 0.5 : 0.34;
    var lumaType = intra && this._qscale < 24 ? 2 : 1;
    double distortion = 0;
    var bits = 0;
    Span<int> residual = stackalloc int[16];
    Span<double> rebuilt = stackalloc double[16];

    // Luminance: sixteen 4x4 blocks in H.264's block order, each a residual over its prediction.
    var blocks = new short[16][];
    var lumaCbp = 0;
    var groupBits = new int[4];
    var groupGain = new double[4];
    var groupAny = new bool[4];
    for (var k = 0; k < 16; ++k) {
      int bx = 4 * ((Svq3Decoder.Scan8[k] - Svq3Decoder.Scan8[0]) & 7), by = 4 * ((Svq3Decoder.Scan8[k] - Svq3Decoder.Scan8[0]) >> 3);
      for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 4; ++x)
          residual[4 * y + x] = luma[16 * (by + y) + bx + x] - predictionLuma[16 * (by + y) + bx + x];
      blocks[k] = _Forward(residual, lumaQmul, rounding, skipDc: false);
      groupAny[k >> 2] |= _Encodable(blocks[k], lumaType, 0);
      _Inverse(blocks[k], lumaQmul, rebuilt);
      double before = 0, after = 0;
      for (var i = 0; i < 16; ++i) {
        before += residual[i] * residual[i];
        after += (residual[i] - rebuilt[i]) * (residual[i] - rebuilt[i]);
      }

      distortion += before;
      groupGain[k >> 2] += before - after;
      groupBits[k >> 2] += _Bits(blocks[k], lumaType, 0);
    }

    // An 8x8 group is coded only where what it buys outweighs what it costs.
    for (var group = 0; group < 4; ++group) {
      if (!groupAny[group] || groupGain[group] <= this._Lambda * groupBits[group])
        continue;
      lumaCbp |= 1 << group;
      distortion -= groupGain[group];
      bits += groupBits[group];
    }

    for (var group = 0; group < 4; ++group)
      if ((lumaCbp & (1 << group)) == 0)
        for (var j = 0; j < 4; ++j)
          Array.Clear(blocks[4 * group + j]);

    // Chrominance: a 2x2 DC transform per plane and the AC of four 4x4 blocks.
    var dc = new short[2][];
    var ac = new short[2][][];
    double chromaBefore = 0, chromaDcOnly = 0, chromaFull = 0;
    int dcBits = 0, acBits = 0;
    var anyDc = false;
    var anyAc = false;
    for (var p = 0; p < 2; ++p) {
      var plane = p == 0 ? cb : cr;
      var prediction = p == 0 ? predictionCb : predictionCr;
      var residuals = new int[4][];
      Span<double> mean = stackalloc double[4];
      ac[p] = new short[4][];
      for (var j = 0; j < 4; ++j) {
        int bx = 4 * (j & 1), by = 4 * (j >> 1);
        residuals[j] = new int[16];
        var sum = 0;
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x) {
            residuals[j][4 * y + x] = plane[8 * (by + y) + bx + x] - prediction[8 * (by + y) + bx + x];
            sum += residuals[j][4 * y + x];
          }

        mean[j] = sum / 16.0;
        ac[p][j] = _Forward(residuals[j], chromaQmul, rounding, skipDc: true);
        anyAc |= _Encodable(ac[p][j], 1, 1);
        acBits += _Bits(ac[p][j], 1, 1);
      }

      dc[p] = _ChromaDc(mean, chromaQmul);
      _Encodable(dc[p], 3, 0);
      dcBits += _Bits(dc[p], 3, 0);
      anyDc |= dc[p][0] != 0 || dc[p][1] != 0 || dc[p][2] != 0 || dc[p][3] != 0;

      int a = dc[p][0], b = dc[p][1], c = dc[p][2], d = dc[p][3];
      ReadOnlySpan<int> hadamard = [a + b + c + d, a - b + c - d, a + b - c - d, a - b - c + d];
      for (var j = 0; j < 4; ++j) {
        var offset = 169.0 * chromaQmul * hadamard[j] / (1 << 21);
        _Inverse(ac[p][j], chromaQmul, rebuilt);
        for (var i = 0; i < 16; ++i) {
          var r = residuals[j][i];
          chromaBefore += r * r;
          chromaDcOnly += (r - offset) * (r - offset);
          chromaFull += (r - offset - rebuilt[i]) * (r - offset - rebuilt[i]);
        }
      }
    }

    var chroma = 0;
    var chromaCost = chromaBefore;
    if (anyDc && chromaDcOnly + this._Lambda * dcBits < chromaCost) {
      chroma = 1;
      chromaCost = chromaDcOnly + this._Lambda * dcBits;
    }

    if (anyAc && chromaFull + this._Lambda * (dcBits + acBits) < chromaCost) {
      chroma = 2;
      chromaCost = chromaFull + this._Lambda * (dcBits + acBits);
    }

    if (chroma == 0)
      for (var p = 0; p < 2; ++p)
        Array.Clear(dc[p]);
    if (chroma < 2)
      for (var p = 0; p < 2; ++p)
        foreach (var block in ac[p])
          Array.Clear(block);

    var cbp = lumaCbp | chroma << 4;
    var header = intra
      ? _UeLength(key ? _INTRA_RAW_TYPE_I : _INTRA_RAW_TYPE_P) + _UeLength((uint)Array.IndexOf(Svq3Tables.GolombToIntra4x4Cbp, (byte)cbp))
      : _UeLength(_INTER_RAW_TYPE) + 2 + _UeLength((uint)Array.IndexOf(Svq3Tables.GolombToInterCbp, (byte)cbp));
    return new(intra, lumaType, blocks, dc, ac, cbp, distortion + chromaCost + this._Lambda * (bits + header));
  }

  /// <summary>The levels' contribution to a block, unrounded: <c>qmul / 2^20</c> times the basis products.</summary>
  private static void _Inverse(short[] block, uint qmul, Span<double> target) {
    var scale = qmul / (double)(1 << 20);
    target.Clear();
    for (var v = 0; v < 4; ++v)
      for (var u = 0; u < 4; ++u) {
        var level = block[u + 4 * v];
        if (level == 0)
          continue;
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            target[4 * y + x] += level * scale * _Basis[4 * u + x] * _Basis[4 * v + y];
      }
  }

  private static long _Sse(int[] a, int[] b) {
    long sum = 0;
    for (var i = 0; i < a.Length; ++i)
      sum += (long)(a[i] - b[i]) * (a[i] - b[i]);
    return sum;
  }

  private static int _Bits(short[] block, int type, int start) {
    var bits = 0;
    _Walk(block, type, start, (index, sign, vlc) => bits += _UeLength(index < 0 ? 0 : sign > 0 ? 2 * vlc - 1 : 2 * vlc));
    return bits;
  }

  private static int _UeLength(uint value) => 2 * (31 - int.LeadingZeroCount((int)(value + 1))) + 1;

  /// <summary>
  /// The coefficients whose <c>svq3_add_idct_c</c> comes closest to <paramref name="residual"/>: the
  /// transform's basis vectors are orthogonal with a common norm of 676 in each direction, and the
  /// inverse scales by <c>qmul / 2^20</c>.
  /// </summary>
  private static short[] _Forward(ReadOnlySpan<int> residual, uint qmul, double rounding, bool skipDc) {
    var result = new short[16];
    var scale = (double)(1 << 20) / (676.0 * 676.0 * qmul);
    for (var v = 0; v < 4; ++v)
      for (var u = 0; u < 4; ++u) {
        if (skipDc && u == 0 && v == 0)
          continue;
        double sum = 0;
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            sum += residual[4 * y + x] * _Basis[4 * u + x] * _Basis[4 * v + y];
        var value = sum * scale;
        var level = (int)(Math.Abs(value) + rounding);
        result[u + 4 * v] = (short)(Math.Sign(value) * Math.Min(level, 255));
      }

    return result;
  }

  /// <summary>
  /// The four chroma DC levels: a block's mean comes back as <c>169 * qmul * H / 2^21</c>, where
  /// <c>H</c> is that block's entry in the 2x2 Hadamard transform of the levels.
  /// </summary>
  private static short[] _ChromaDc(ReadOnlySpan<double> mean, uint qmul) {
    var scale = (double)(1 << 21) / (169.0 * qmul);
    Span<double> target = stackalloc double[4];
    for (var j = 0; j < 4; ++j)
      target[j] = mean[j] * scale;

    // ff_h264_chroma_dc_dequant_idct puts (a+b)+(c+d), (a-b)+(c-d), (a+b)-(c+d) and (a-b)-(c-d) in
    // blocks 0 to 3; the transform is its own inverse up to a factor of four.
    double a = (target[0] + target[1] + target[2] + target[3]) / 4;
    double b = (target[0] - target[1] + target[2] - target[3]) / 4;
    double c = (target[0] + target[1] - target[2] - target[3]) / 4;
    double d = (target[0] - target[1] - target[2] + target[3]) / 4;
    static short Level(double v) => (short)(Math.Sign(v) * Math.Min((int)(Math.Abs(v) + 0.5), 255));
    // The levels arrive in ff_h264_chroma_dc_scan order: blocks 0, 1, 2, 3 of the plane.
    return [Level(a), Level(b), Level(c), Level(d)];
  }

  // ============================================================================================
  // Coefficient coding: the inverse of svq3_decode_block
  // ============================================================================================

  private static ReadOnlySpan<byte> _ScanOf(int type) => type switch {
    0 => Svq3Tables.LumaDcZigzagScan,
    1 => Svq3Tables.ZigzagScan,
    2 => Svq3Tables.Svq3Scan,
    _ => [0, 1, 2, 3],
  };

  /// <summary>The codes of one table: which magnitude index states a run and a level.</summary>
  private static readonly Dictionary<(int Run, int Level), uint>[] _Codes = _BuildCodes();

  private static Dictionary<(int, int), uint>[] _BuildCodes() {
    var result = new Dictionary<(int, int), uint>[4];
    for (var type = 1; type < 4; ++type) {
      var codes = new Dictionary<(int, int), uint>();
      var intra = 3 * type >> 2;
      for (var vlc = 1u; vlc < 1u << 13; ++vlc) {
        int run, level;
        if (type == 3) {
          if (vlc < 3) {
            run = 0;
            level = (int)vlc;
          } else if (vlc < 4) {
            run = 1;
            level = 1;
          } else {
            run = (int)(vlc & 3);
            level = (int)((vlc + 9) >> 2) - run;
          }
        } else if (vlc < 16) {
          run = Svq3Tables.DctRun[16 * intra + vlc];
          level = Svq3Tables.DctLevel[16 * intra + vlc];
        } else if (intra != 0) {
          run = (int)(vlc & 7);
          level = (int)(vlc >> 3) + (run == 0 ? 8 : run < 2 ? 2 : run < 5 ? 0 : -1);
        } else {
          run = (int)(vlc & 15);
          level = (int)(vlc >> 4) + (run == 0 ? 4 : run < 3 ? 2 : run < 10 ? 1 : 0);
        }

        if (level > 0)
          codes.TryAdd((run, level), vlc);
      }

      result[type] = codes;
    }

    return result;
  }

  /// <summary>Whether a block has a level the coding can state, dropping the ones it cannot.</summary>
  private static bool _Encodable(short[] block, int type, int start) {
    var any = false;
    _Walk(block, type, start, (index, _, _) => any |= index >= 0);
    return any;
  }

  /// <summary>
  /// Visits the levels <c>svq3_decode_block</c> would read, in order, with their runs; a level no code
  /// states (a run too long for the table, or a level too large for it) is set to nought and the walk
  /// goes on, so the block holds exactly what is coded.
  /// </summary>
  private static void _Walk(short[] block, int type, int start, Action<int, int, uint> visit) {
    var scan = _ScanOf(type).ToArray();
    var codes = _Codes[type];
    var intra = 3 * type >> 2;
    var index = start;
    for (var limit = 16 >> intra; index < 16; index = limit, limit += 8) {
      var run = 0;
      for (; index < limit; ++index) {
        int level = block[scan[index]];
        if (level == 0) {
          ++run;
          continue;
        }

        var magnitude = Math.Abs(level);
        uint vlc = 0;
        while (magnitude > 0 && !codes.TryGetValue((run, magnitude), out vlc))
          --magnitude;
        if (magnitude == 0) {
          block[scan[index]] = 0;
          ++run;
          continue;
        }

        block[scan[index]] = (short)(level < 0 ? -magnitude : magnitude);
        visit(index, level < 0 ? -1 : 1, vlc);
        run = 0;
      }

      visit(-1, 0, 0);
      if (type != 2)
        break;
    }
  }

  private static void _Block(MsMpeg4BitWriter output, short[] block, int start, int type)
    => _Walk(block, type, start, (index, sign, vlc) => _Ue(output, index < 0 ? 0 : sign > 0 ? 2 * vlc - 1 : 2 * vlc));

  /// <summary>An interleaved exp-Golomb code: each bit after the leading one is preceded by a nought, and a one ends it.</summary>
  private static void _Ue(MsMpeg4BitWriter output, uint value) {
    var code = value + 1;
    var length = 31 - int.LeadingZeroCount((int)code);
    for (var i = length - 1; i >= 0; --i) {
      output.Write(0, 1);
      output.Write((int)(code >> i) & 1, 1);
    }

    output.Write(1, 1);
  }

  /// <summary>The input planes, read with their edges repeated out to whole macroblocks.</summary>
  private readonly ref struct Source(ReadOnlySpan<byte> planes, int width, int height) {
    private readonly ReadOnlySpan<byte> _planes = planes;

    internal void Block(int plane, int x0, int y0, int size, Span<int> target) {
      int chromaWidth = (width + 1) / 2, chromaHeight = (height + 1) / 2;
      var (offset, w, h) = plane switch {
        0 => (0, width, height),
        1 => (width * height, chromaWidth, chromaHeight),
        _ => (width * height + chromaWidth * chromaHeight, chromaWidth, chromaHeight),
      };
      for (var y = 0; y < size; ++y) {
        var row = offset + Math.Min(y0 + y, h - 1) * w;
        for (var x = 0; x < size; ++x)
          target[size * y + x] = this._planes[row + Math.Min(x0 + x, w - 1)];
      }
    }
  }
}
