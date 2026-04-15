using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// VP8 lossy decoder per RFC 6386. Faithful port of golang.org/x/image/vp8 by Nigel Tao,
/// which itself follows libwebp's implementation. Handles keyframes only (still images);
/// Golden/AltRef frames used by video are not supported.
/// </summary>
internal sealed partial class Vp8Decoder {

  // --- Constants (from decode.go / token.go / reconstruct.go) ---

  private const int NSegment = 4;
  private const int NSegmentProb = 3;
  private const int NRefLfDelta = 4;
  private const int NModeLfDelta = 4;

  private const int NPlane = 4;
  private const int NBand = 8;
  private const int NContext = 3;
  private const int NProb = 11;

  private const int PlaneY1WithY2 = 0;
  private const int PlaneY2 = 1;
  private const int PlaneUV = 2;
  private const int PlaneY1SansY2 = 3;

  // Coefficient layout: 16 luma DCT blocks, 4 U + 4 V chroma, 1 Y2 WHT block.
  private const int CoeffCount = 1 * 16 * 16 + 2 * 8 * 8 + 1 * 4 * 4; // 400
  private const int BCoeffBase = 1 * 16 * 16 + 0 * 8 * 8;              // 256
  private const int RCoeffBase = 1 * 16 * 16 + 1 * 8 * 8;              // 320
  private const int WhtCoeffBase = 1 * 16 * 16 + 2 * 8 * 8;            // 384

  private const int YbrYX = 8;
  private const int YbrYY = 1;
  private const int YbrBX = 8;
  private const int YbrBY = 18;
  private const int YbrRX = 24;
  private const int YbrRY = 18;

  // --- Nested types (port of Go struct fields) ---

  internal struct FrameHeader {
    public bool KeyFrame;
    public byte VersionNumber;
    public bool ShowFrame;
    public uint FirstPartitionLen;
    public int Width;
    public int Height;
    public byte XScale;
    public byte YScale;
  }

  private struct SegmentHeader {
    public bool UseSegment;
    public bool UpdateMap;
    public bool RelativeDelta;
    public sbyte Quantizer0, Quantizer1, Quantizer2, Quantizer3;
    public sbyte FilterStrength0, FilterStrength1, FilterStrength2, FilterStrength3;
    public byte Prob0, Prob1, Prob2;

    public sbyte GetQuantizer(int i) => i switch {
      0 => Quantizer0, 1 => Quantizer1, 2 => Quantizer2, _ => Quantizer3,
    };
    public void SetQuantizer(int i, sbyte v) {
      switch (i) { case 0: Quantizer0 = v; break; case 1: Quantizer1 = v; break; case 2: Quantizer2 = v; break; default: Quantizer3 = v; break; }
    }
    public sbyte GetFilterStrength(int i) => i switch {
      0 => FilterStrength0, 1 => FilterStrength1, 2 => FilterStrength2, _ => FilterStrength3,
    };
    public void SetFilterStrength(int i, sbyte v) {
      switch (i) { case 0: FilterStrength0 = v; break; case 1: FilterStrength1 = v; break; case 2: FilterStrength2 = v; break; default: FilterStrength3 = v; break; }
    }
    public byte GetProb(int i) => i switch { 0 => Prob0, 1 => Prob1, _ => Prob2 };
    public void SetProb(int i, byte v) {
      switch (i) { case 0: Prob0 = v; break; case 1: Prob1 = v; break; default: Prob2 = v; break; }
    }
  }

  private struct FilterHeader {
    public bool Simple;
    public sbyte Level;
    public byte Sharpness;
    public bool UseLfDelta;
    public sbyte RefLfDelta0, RefLfDelta1, RefLfDelta2, RefLfDelta3;
    public sbyte ModeLfDelta0, ModeLfDelta1, ModeLfDelta2, ModeLfDelta3;
    public sbyte PerSegmentLevel0, PerSegmentLevel1, PerSegmentLevel2, PerSegmentLevel3;

    public sbyte GetRefLfDelta(int i) => i switch {
      0 => RefLfDelta0, 1 => RefLfDelta1, 2 => RefLfDelta2, _ => RefLfDelta3,
    };
    public void SetRefLfDelta(int i, sbyte v) {
      switch (i) { case 0: RefLfDelta0 = v; break; case 1: RefLfDelta1 = v; break; case 2: RefLfDelta2 = v; break; default: RefLfDelta3 = v; break; }
    }
    public sbyte GetModeLfDelta(int i) => i switch {
      0 => ModeLfDelta0, 1 => ModeLfDelta1, 2 => ModeLfDelta2, _ => ModeLfDelta3,
    };
    public void SetModeLfDelta(int i, sbyte v) {
      switch (i) { case 0: ModeLfDelta0 = v; break; case 1: ModeLfDelta1 = v; break; case 2: ModeLfDelta2 = v; break; default: ModeLfDelta3 = v; break; }
    }
    public void SetPerSegmentLevel(int i, sbyte v) {
      switch (i) { case 0: PerSegmentLevel0 = v; break; case 1: PerSegmentLevel1 = v; break; case 2: PerSegmentLevel2 = v; break; default: PerSegmentLevel3 = v; break; }
    }
  }

  private struct Quant {
    public ushort Y1Dc, Y1Ac;
    public ushort Y2Dc, Y2Ac;
    public ushort UvDc, UvAc;
  }

  private struct FilterParam {
    public byte Level, Ilevel, Hlevel;
    public bool Inner;
  }

  /// <summary>Per-macroblock decode state: one per column (upMB) and one current-row-left (leftMB).</summary>
  private struct Mb {
    public byte Pred0, Pred1, Pred2, Pred3; // 4x4 luma region predictor modes
    public byte NzMask;                      // 4 luma + 2 U + 2 V non-zero flags
    public byte NzY16;                       // 1 iff Y16-prediction with non-zero DC

    public byte GetPred(int i) => i switch { 0 => Pred0, 1 => Pred1, 2 => Pred2, _ => Pred3 };
    public void SetPred(int i, byte v) {
      switch (i) { case 0: Pred0 = v; break; case 1: Pred1 = v; break; case 2: Pred2 = v; break; default: Pred3 = v; break; }
    }
  }

  // --- State (maps 1:1 to Go's Decoder struct) ---

  private byte[] _src = [];   // Raw VP8 chunk bytes.
  private int _srcPos;         // Cursor during header parsing.
  private int _srcEnd;         // End of VP8 chunk (Go's limitReader.n tracks remaining).

  private FrameHeader _fh;
  private SegmentHeader _segmentHeader;
  private FilterHeader _filterHeader;

  private readonly Vp8Partition _fp = new();
  private readonly Vp8Partition[] _op = new Vp8Partition[8];
  private int _nOp;

  // YCbCr output buffers. Sized to (16*mbw, 16*mbh) for Y and (8*mbw, 8*mbh) for Cb/Cr.
  private byte[] _yPlane = [];
  private byte[] _cbPlane = [];
  private byte[] _crPlane = [];
  private int _yStride;
  private int _cStride;

  private int _mbw, _mbh;

  private readonly Quant[] _quant = new Quant[NSegment];
  // Token probabilities: 4 planes * 8 bands * 3 contexts * 11 probs.
  private readonly byte[] _tokenProb = new byte[NPlane * NBand * NContext * NProb];
  private bool _useSkipProb;
  private byte _skipProb;

  private readonly FilterParam[] _filterParams = new FilterParam[NSegment * 2];
  private FilterParam[] _perMBFilterParams = [];

  private int _segment;
  private Mb _leftMB;
  private Mb[] _upMB = [];
  private uint _nzDcMask, _nzAcMask;

  private bool _usePredY16;
  private byte _predY16;
  private byte _predC8;
  // predY4[j][i] = pred mode for luma 4x4 block (i, j). Packed as row-major 16 bytes.
  private readonly byte[] _predY4 = new byte[16];

  // Workspace: coefficients and reconstruction scratch. ybr = 26 rows x 32 cols.
  private readonly short[] _coeff = new short[CoeffCount];
  private readonly byte[] _ybr = new byte[26 * 32];

  public Vp8Decoder() {
    for (var i = 0; i < 8; ++i)
      _op[i] = new Vp8Partition();
  }

  // --- Public entry points ---

  /// <summary>Decode a complete VP8 chunk into an RGB24 byte array.</summary>
  public static byte[] Decode(byte[] vp8ChunkData, int expectedWidth, int expectedHeight) {
    ArgumentNullException.ThrowIfNull(vp8ChunkData);
    var d = new Vp8Decoder();
    d._Init(vp8ChunkData);
    d._DecodeFrameHeader();
    if (d._fh.Width != expectedWidth || d._fh.Height != expectedHeight)
      throw new InvalidDataException(
        $"VP8 header dimensions ({d._fh.Width}x{d._fh.Height}) do not match RIFF dimensions ({expectedWidth}x{expectedHeight}).");
    d._DecodeFrame();
    return d._YuvToRgb24();
  }

  // --- Internal helpers ---

  private void _Init(byte[] chunk) {
    _src = chunk;
    _srcPos = 0;
    _srcEnd = chunk.Length;
  }

  /// <summary>Read exactly n bytes from the input chunk into a new array. Mirrors Go's limitReader.ReadFull.</summary>
  private byte[] _ReadFull(int n) {
    if (n > _srcEnd - _srcPos)
      throw new EndOfStreamException("VP8 chunk shorter than header advertises.");
    var buf = new byte[n];
    Array.Copy(_src, _srcPos, buf, 0, n);
    _srcPos += n;
    return buf;
  }

  /// <summary>Parse the 3-byte frame tag and 7-byte keyframe header. Port of DecodeFrameHeader.</summary>
  private void _DecodeFrameHeader() {
    var b = _ReadFull(3);
    _fh.KeyFrame = (b[0] & 1) == 0;
    _fh.VersionNumber = (byte)((b[0] >> 1) & 7);
    _fh.ShowFrame = ((b[0] >> 4) & 1) == 1;
    _fh.FirstPartitionLen = (uint)b[0] >> 5 | (uint)b[1] << 3 | (uint)b[2] << 11;
    if (!_fh.KeyFrame)
      throw new InvalidDataException("VP8 still image must start with a keyframe.");
    var kh = _ReadFull(7);
    if (kh[0] != 0x9d || kh[1] != 0x01 || kh[2] != 0x2a)
      throw new InvalidDataException("VP8: invalid keyframe start code (expected 0x9D 0x01 0x2A).");
    _fh.Width = (kh[4] & 0x3f) << 8 | kh[3];
    _fh.Height = (kh[6] & 0x3f) << 8 | kh[5];
    _fh.XScale = (byte)(kh[4] >> 6);
    _fh.YScale = (byte)(kh[6] >> 6);
    _mbw = (_fh.Width + 0x0f) >> 4;
    _mbh = (_fh.Height + 0x0f) >> 4;
    _segmentHeader = default;
    _segmentHeader.Prob0 = 0xff;
    _segmentHeader.Prob1 = 0xff;
    _segmentHeader.Prob2 = 0xff;
    Buffer.BlockCopy(DefaultTokenProb, 0, _tokenProb, 0, _tokenProb.Length);
    _segment = 0;
  }

  /// <summary>Allocate image and per-macroblock buffers. Port of ensureImg.</summary>
  private void _EnsureImg() {
    _yStride = 16 * _mbw;
    _cStride = 8 * _mbw;
    _yPlane = new byte[_yStride * 16 * _mbh];
    _cbPlane = new byte[_cStride * 8 * _mbh];
    _crPlane = new byte[_cStride * 8 * _mbh];
    _perMBFilterParams = new FilterParam[_mbw * _mbh];
    _upMB = new Mb[_mbw];
  }

  /// <summary>Decode macroblocks + apply loop filter. Port of DecodeFrame.</summary>
  private void _DecodeFrame() {
    _EnsureImg();
    _ParseOtherHeaders();
    for (var mbx = 0; mbx < _mbw; ++mbx)
      _upMB[mbx] = default;
    for (var mby = 0; mby < _mbh; ++mby) {
      _leftMB = default;
      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var skip = _Reconstruct(mbx, mby);
        ref var fs = ref _filterParams[_segment * 2 + (_usePredY16 ? 0 : 1)];
        var fp = fs;
        fp.Inner = fp.Inner || !skip;
        _perMBFilterParams[_mbw * mby + mbx] = fp;
      }
    }
    if (_fp.UnexpectedEof) throw new EndOfStreamException("Unexpected EOF in VP8 first partition.");
    for (var i = 0; i < _nOp; ++i)
      if (_op[i].UnexpectedEof) throw new EndOfStreamException($"Unexpected EOF in VP8 coefficient partition {i}.");
    if (_filterHeader.Level != 0) {
      if (_filterHeader.Simple)
        _SimpleFilter();
      else
        _NormalFilter();
    }
  }

  /// <summary>Port of parseOtherHeaders.</summary>
  private void _ParseOtherHeaders() {
    var firstPartition = _ReadFull((int)_fh.FirstPartitionLen);
    _fp.Init(firstPartition);
    // Keyframes: 1 bit color-space, 1 bit pixel-clamp (both ignored).
    _fp.ReadBit(Vp8Partition.UniformProb);
    _fp.ReadBit(Vp8Partition.UniformProb);
    _ParseSegmentHeader();
    _ParseFilterHeader();
    _ParseOtherPartitions();
    _ParseQuant();
    // Non-keyframe flag (refreshLastFrameBuffer) — we reject non-keyframes in header, but
    // still need to consume the bit per spec for keyframes.
    _fp.ReadBit(Vp8Partition.UniformProb);
    _ParseTokenProb();
    _useSkipProb = _fp.ReadBit(Vp8Partition.UniformProb);
    if (_useSkipProb)
      _skipProb = (byte)_fp.ReadUint(Vp8Partition.UniformProb, 8);
    if (_fp.UnexpectedEof)
      throw new EndOfStreamException("Unexpected EOF while parsing VP8 headers.");
  }

  /// <summary>Port of parseSegmentHeader.</summary>
  private void _ParseSegmentHeader() {
    _segmentHeader.UseSegment = _fp.ReadBit(Vp8Partition.UniformProb);
    if (!_segmentHeader.UseSegment) {
      _segmentHeader.UpdateMap = false;
      return;
    }
    _segmentHeader.UpdateMap = _fp.ReadBit(Vp8Partition.UniformProb);
    if (_fp.ReadBit(Vp8Partition.UniformProb)) {
      _segmentHeader.RelativeDelta = !_fp.ReadBit(Vp8Partition.UniformProb);
      for (var i = 0; i < NSegment; ++i)
        _segmentHeader.SetQuantizer(i, (sbyte)_fp.ReadOptionalInt(Vp8Partition.UniformProb, 7));
      for (var i = 0; i < NSegment; ++i)
        _segmentHeader.SetFilterStrength(i, (sbyte)_fp.ReadOptionalInt(Vp8Partition.UniformProb, 6));
    }
    if (!_segmentHeader.UpdateMap) return;
    for (var i = 0; i < NSegmentProb; ++i) {
      if (_fp.ReadBit(Vp8Partition.UniformProb))
        _segmentHeader.SetProb(i, (byte)_fp.ReadUint(Vp8Partition.UniformProb, 8));
      else
        _segmentHeader.SetProb(i, 0xff);
    }
  }

  /// <summary>Port of parseFilterHeader.</summary>
  private void _ParseFilterHeader() {
    _filterHeader.Simple = _fp.ReadBit(Vp8Partition.UniformProb);
    _filterHeader.Level = (sbyte)_fp.ReadUint(Vp8Partition.UniformProb, 6);
    _filterHeader.Sharpness = (byte)_fp.ReadUint(Vp8Partition.UniformProb, 3);
    _filterHeader.UseLfDelta = _fp.ReadBit(Vp8Partition.UniformProb);
    if (_filterHeader.UseLfDelta && _fp.ReadBit(Vp8Partition.UniformProb)) {
      for (var i = 0; i < NRefLfDelta; ++i)
        _filterHeader.SetRefLfDelta(i, (sbyte)_fp.ReadOptionalInt(Vp8Partition.UniformProb, 6));
      for (var i = 0; i < NModeLfDelta; ++i)
        _filterHeader.SetModeLfDelta(i, (sbyte)_fp.ReadOptionalInt(Vp8Partition.UniformProb, 6));
    }
    if (_filterHeader.Level == 0) return;
    if (_segmentHeader.UseSegment) {
      for (var i = 0; i < NSegment; ++i) {
        var strength = _segmentHeader.GetFilterStrength(i);
        if (_segmentHeader.RelativeDelta)
          strength = (sbyte)(strength + _filterHeader.Level);
        _filterHeader.SetPerSegmentLevel(i, strength);
      }
    } else {
      _filterHeader.PerSegmentLevel0 = _filterHeader.Level;
    }
    _ComputeFilterParams();
  }

  /// <summary>Port of parseOtherPartitions: split the rest of the chunk into nOp coefficient partitions.</summary>
  private void _ParseOtherPartitions() {
    const int maxNOp = 1 << 3;
    Span<int> partLens = stackalloc int[maxNOp];
    _nOp = 1 << (int)_fp.ReadUint(Vp8Partition.UniformProb, 2);
    var n = 3 * (_nOp - 1);
    var remaining = _srcEnd - _srcPos;
    partLens[_nOp - 1] = remaining - n;
    if (partLens[_nOp - 1] < 0)
      throw new EndOfStreamException("VP8: too little data for partition header.");
    if (n > 0) {
      var hdr = _ReadFull(n);
      for (var i = 0; i < _nOp - 1; ++i) {
        var pl = hdr[3 * i + 0] | hdr[3 * i + 1] << 8 | hdr[3 * i + 2] << 16;
        if (pl > partLens[_nOp - 1]) throw new EndOfStreamException("VP8: partition length exceeds remaining data.");
        partLens[i] = pl;
        partLens[_nOp - 1] -= pl;
      }
    }
    if (partLens[_nOp - 1] >= 1 << 24)
      throw new InvalidDataException("VP8: final partition too large.");
    var rest = _ReadFull(_srcEnd - _srcPos);
    var off = 0;
    for (var i = 0; i < _nOp; ++i) {
      var pl = partLens[i];
      var pbuf = new byte[pl];
      Array.Copy(rest, off, pbuf, 0, pl);
      _op[i].Init(pbuf);
      off += pl;
    }
  }

  // --- YUV → RGB24 (BT.601 studio range, matching libwebp's VP8YuvToRgb) ---
  // Y in [16, 235], UV in [16, 240]. Fixed-point formula from libwebp src/dsp/yuv.h:
  //   R = Clip8(MultHi(y,19077) + MultHi(v,26149) - 14234)
  //   G = Clip8(MultHi(y,19077) - MultHi(u,6419) - MultHi(v,13320) + 8708)
  //   B = Clip8(MultHi(y,19077) + MultHi(u,33050) - 17685)
  // where MultHi(a,b) = (a*b)>>8 and Clip8(v) = v>>6 if in [0, 16383], else saturate to [0,255].

  private byte[] _YuvToRgb24() {
    var w = _fh.Width;
    var h = _fh.Height;
    var rgb = new byte[w * h * 3];
    for (var y = 0; y < h; ++y) {
      var yRow = y * _yStride;
      var cRow = (y >> 1) * _cStride;
      for (var x = 0; x < w; ++x) {
        int Y = _yPlane[yRow + x];
        int u = _cbPlane[cRow + (x >> 1)];
        int v = _crPlane[cRow + (x >> 1)];
        var o = (y * w + x) * 3;
        rgb[o + 0] = _YuvToR(Y, v);
        rgb[o + 1] = _YuvToG(Y, u, v);
        rgb[o + 2] = _YuvToB(Y, u);
      }
    }
    return rgb;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _MultHi(int v, int coeff) => v * coeff >> 8;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _YuvClip8(int v) => (v & ~16383) == 0 ? (byte)(v >> 6) : v < 0 ? (byte)0 : (byte)255;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _YuvToR(int y, int v) => _YuvClip8(_MultHi(y, 19077) + _MultHi(v, 26149) - 14234);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _YuvToG(int y, int u, int v) => _YuvClip8(_MultHi(y, 19077) - _MultHi(u, 6419) - _MultHi(v, 13320) + 8708);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _YuvToB(int y, int u) => _YuvClip8(_MultHi(y, 19077) + _MultHi(u, 33050) - 17685);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static byte _Clip8(int x) => x < 0 ? (byte)0 : x > 255 ? (byte)255 : (byte)x;

  // --- ybr workspace accessors ---
  // ybr is 26 rows × 32 cols. Index helper for clarity.
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int Ybr(int y, int x) => y * 32 + x;

  // --- coeff/predY4 helpers (3D indexing into flat arrays) ---

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int TpIdx(int plane, int band, int ctx, int i) =>
    ((plane * NBand + band) * NContext + ctx) * NProb + i;
}
