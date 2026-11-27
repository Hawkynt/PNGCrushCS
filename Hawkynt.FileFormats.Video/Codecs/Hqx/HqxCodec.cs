using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Hqx;

/// <summary>The four layouts an HQX frame states, all twelve bits a sample.</summary>
internal enum HqxFormat { Yuv422 = 0, Yuv444 = 1, Yuv422Alpha = 2, Yuv444Alpha = 3 }

/// <summary>Reads bits most-significant first; past the end every bit is zero.</summary>
internal ref struct HqxBitReader(ReadOnlySpan<byte> data) {
  private readonly ReadOnlySpan<byte> _data = data;
  private int _bit;

  internal readonly int Peek(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var at = this._bit + i;
      value = value << 1 | (at >> 3 < this._data.Length ? (this._data[at >> 3] >> (7 - (at & 7))) & 1 : 0);
    }
    return value;
  }

  internal void Skip(int count) => this._bit += count;

  internal int Read(int count) {
    var value = this.Peek(count);
    this._bit += count;
    return value;
  }
}

/// <summary>Writes bits most-significant first.</summary>
internal sealed class HqxBitWriter {
  private readonly List<byte> _bytes = [];
  private int _current;
  private int _filled;

  internal void Write(int value, int count) {
    for (var i = count - 1; i >= 0; --i) {
      this._current = this._current << 1 | ((value >> i) & 1);
      if (++this._filled == 8) {
        this._bytes.Add((byte)this._current);
        this._current = 0;
        this._filled = 0;
      }
    }
  }

  internal byte[] ToArray() {
    var result = new List<byte>(this._bytes);
    if (this._filled != 0)
      result.Add((byte)(this._current << (8 - this._filled)));
    return [.. result];
  }
}

/// <summary>A prefix code read most-significant bit first, looked up a bit at a time.</summary>
internal sealed class HqxVlc {
  private readonly Dictionary<long, int> _symbols = [];
  private readonly Dictionary<int, (int Code, int Length)> _codes = [];
  private readonly int _longest;

  private HqxVlc(int longest) => this._longest = longest;

  /// <summary>Codes given outright, the symbol being the index, as FFmpeg's <c>vlc_init</c> takes them.</summary>
  internal static HqxVlc FromCodes(ReadOnlySpan<ushort> codes, ReadOnlySpan<byte> lengths) {
    var longest = 0;
    foreach (var length in lengths)
      longest = Math.Max(longest, length);
    var vlc = new HqxVlc(longest);
    for (var i = 0; i < codes.Length; ++i)
      if (lengths[i] > 0)
        vlc._Add(i, codes[i], lengths[i]);
    return vlc;
  }

  /// <summary>Canonical codes from lengths in listed order, as <c>ff_vlc_init_from_lengths</c> assigns them.</summary>
  internal static HqxVlc FromLengths(ReadOnlySpan<byte> lengths, ReadOnlySpan<int> symbols) {
    var longest = 0;
    foreach (var length in lengths)
      longest = Math.Max(longest, length);
    var vlc = new HqxVlc(longest);
    ulong code = 0;
    for (var i = 0; i < lengths.Length; ++i) {
      int length = lengths[i];
      if (length == 0)
        continue;
      vlc._Add(symbols[i], (int)(code >> (32 - length)), length);
      code += 1UL << (32 - length);
    }
    return vlc;
  }

  private void _Add(int symbol, int code, int length) {
    this._symbols[(long)length << 32 | (uint)code] = symbol;
    this._codes.TryAdd(symbol, (code, length));
  }

  internal int Read(ref HqxBitReader reader) {
    var code = 0;
    for (var length = 1; length <= this._longest; ++length) {
      code = code << 1 | reader.Read(1);
      if (this._symbols.TryGetValue((long)length << 32 | (uint)code, out var symbol))
        return symbol;
    }
    throw new InvalidDataException("An HQX code matches no entry of its table.");
  }

  internal bool TryWrite(HqxBitWriter writer, int symbol) {
    if (!this._codes.TryGetValue(symbol, out var code))
      return false;
    writer.Write(code.Code, code.Length);
    return true;
  }
}

/// <summary>The tables built once, the transform, and the order HQX visits its macroblocks in.</summary>
internal static class HqxShared {

  internal const int HeaderLength = 59;
  internal const int Slices = 16;

  internal static readonly HqxVlc[] Dc = [
    HqxVlc.FromCodes(HqxTables.Dc9Codes, HqxTables.Dc9Lengths),
    HqxVlc.FromCodes(HqxTables.Dc10Codes, HqxTables.Dc10Lengths),
    HqxVlc.FromCodes(HqxTables.Dc11Codes, HqxTables.Dc11Lengths),
  ];

  /// <summary>Each AC table's symbols as <c>level * 128 + run + 1</c>, FFmpeg's <c>E()</c>.</summary>
  internal static readonly HqxVlc[] Ac = _BuildAc();

  private static HqxVlc[] _BuildAc() {
    var tables = new HqxVlc[HqxTables.AcSizes.Length];
    var at = 0;
    for (var t = 0; t < tables.Length; ++t) {
      var size = HqxTables.AcSizes[t];
      var symbols = new int[size];
      for (var i = 0; i < size; ++i)
        symbols[i] = HqxTables.AcLevels[at + i] * 128 + HqxTables.AcRuns[at + i] + 1;
      tables[t] = HqxVlc.FromLengths(HqxTables.AcLengths.AsSpan(at, size), symbols);
      at += size;
    }
    return tables;
  }

  /// <summary>FFmpeg's <c>AC_IDX</c>: which AC table a quantiser uses.</summary>
  internal static int AcIndex(int q) => q >= 128 ? 5 : q >= 64 ? 4 : q >= 32 ? 3 : q >= 16 ? 2 : q >= 8 ? 1 : 0;

  /// <summary>The coded-block pattern code: the pattern repeated in both nibbles, as <c>ff_hq_cbp_vlc</c> holds it.</summary>
  internal static int ReadCbp(ref HqxBitReader reader) {
    var (pattern, length) = HqxTables.Cbp[reader.Peek(5)];
    reader.Skip(length);
    return pattern << 4 | pattern;
  }

  internal static void WriteCbp(HqxBitWriter writer, int pattern) {
    for (var i = 0; i < HqxTables.Cbp.Length; ++i)
      if (HqxTables.Cbp[i].Pattern == pattern) {
        var length = HqxTables.Cbp[i].Length;
        writer.Write(i >> (5 - length), length);
        return;
      }
    throw new ArgumentOutOfRangeException(nameof(pattern));
  }

  /// <summary>FFmpeg's <c>ff_zigzag_direct</c>.</summary>
  internal static readonly byte[] ZigZag = [
    0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
  ];

  /// <summary>
  /// FFmpeg's <c>hqx_idct_put</c>: a dequantising column pass whose products wrap as unsigned 32-bit values
  /// and whose results are stored as 16-bit, a row pass stored as 16-bit, and twelve bits widened to sixteen.
  /// </summary>
  internal static void InverseTransform(short[] block, ReadOnlySpan<byte> quant, ushort[] plane, int at, int stride) {
    for (var i = 0; i < 8; ++i) {
      int s0 = block[i] * quant[i], s1 = block[8 + i] * quant[8 + i], s2 = block[16 + i] * quant[16 + i], s3 = block[24 + i] * quant[24 + i];
      int s4 = block[32 + i] * quant[32 + i], s5 = block[40 + i] * quant[40 + i], s6 = block[48 + i] * quant[48 + i], s7 = block[56 + i] * quant[56 + i];
      var t0 = unchecked((int)((uint)s3 * 19266u + (uint)s5 * 12873u)) >> 15;
      var t1 = unchecked((int)((uint)s5 * 19266u - (uint)s3 * 12873u)) >> 15;
      var t2 = (unchecked((int)((uint)s7 * 4520u + (uint)s1 * 22725u)) >> 15) - t0;
      var t3 = (unchecked((int)((uint)s1 * 4520u - (uint)s7 * 22725u)) >> 15) - t1;
      var t4 = t0 * 2 + t2;
      var t5 = t1 * 2 + t3;
      var t6 = t2 - t3;
      var t7 = t3 * 2 + t6;
      var t8 = unchecked((int)((uint)t6 * 11585u)) >> 14;
      var t9 = unchecked((int)((uint)t7 * 11585u)) >> 14;
      var tA = unchecked((int)((uint)s2 * 8867u - (uint)s6 * 21407u)) >> 14;
      var tB = unchecked((int)((uint)s6 * 8867u + (uint)s2 * 21407u)) >> 14;
      var tC = (s0 >> 1) - (s4 >> 1);
      var tD = (s4 >> 1) * 2 + tC;
      var tE = tC - (tA >> 1);
      var tF = tD - (tB >> 1);
      var t10 = tF - t5;
      var t11 = tE - t8;
      var t12 = tE + (tA >> 1) * 2 - t9;
      var t13 = tF + (tB >> 1) * 2 - t4;
      block[i] = unchecked((short)(t13 + t4 * 2));
      block[8 + i] = unchecked((short)(t12 + t9 * 2));
      block[16 + i] = unchecked((short)(t11 + t8 * 2));
      block[24 + i] = unchecked((short)(t10 + t5 * 2));
      block[32 + i] = unchecked((short)t10);
      block[40 + i] = unchecked((short)t11);
      block[48 + i] = unchecked((short)t12);
      block[56 + i] = unchecked((short)t13);
    }
    for (var i = 0; i < 8; ++i) {
      var r = i * 8;
      int b0 = block[r], b1 = block[r + 1], b2 = block[r + 2], b3 = block[r + 3], b4 = block[r + 4], b5 = block[r + 5], b6 = block[r + 6], b7 = block[r + 7];
      var t0 = (b3 * 19266 + b5 * 12873) >> 14;
      var t1 = (b5 * 19266 - b3 * 12873) >> 14;
      var t2 = ((b7 * 4520 + b1 * 22725) >> 14) - t0;
      var t3 = ((b1 * 4520 - b7 * 22725) >> 14) - t1;
      var t4 = t0 * 2 + t2;
      var t5 = t1 * 2 + t3;
      var t6 = t2 - t3;
      var t7 = t3 * 2 + t6;
      var t8 = (t6 * 11585) >> 14;
      var t9 = (t7 * 11585) >> 14;
      var tA = (b2 * 8867 - b6 * 21407) >> 14;
      var tB = (b6 * 8867 + b2 * 21407) >> 14;
      var tC = b0 - b4;
      var tD = b4 * 2 + tC;
      var tE = tC - tA;
      var tF = tD - tB;
      var t10 = tF - t5;
      var t11 = tE - t8;
      var t12 = tE + tA * 2 - t9;
      var t13 = tF + tB * 2 - t4;
      block[r] = unchecked((short)((t13 + t4 * 2 + 4) >> 3));
      block[r + 1] = unchecked((short)((t12 + t9 * 2 + 4) >> 3));
      block[r + 2] = unchecked((short)((t11 + t8 * 2 + 4) >> 3));
      block[r + 3] = unchecked((short)((t10 + t5 * 2 + 4) >> 3));
      block[r + 4] = unchecked((short)((t10 + 4) >> 3));
      block[r + 5] = unchecked((short)((t11 + 4) >> 3));
      block[r + 6] = unchecked((short)((t12 + 4) >> 3));
      block[r + 7] = unchecked((short)((t13 + 4) >> 3));
    }
    for (var i = 0; i < 8; ++i)
      for (var j = 0; j < 8; ++j) {
        var v = Math.Clamp(block[i * 8 + j] + 0x800, 0, 4095);
        plane[at + i * stride + j] = (ushort)((v << 4) | (v >> 8));
      }
  }

  /// <summary>FFmpeg's <c>decode_slice</c> walk: the macroblocks one slice holds, in the order it holds them.</summary>
  internal static List<(int X, int Y)> SliceOrder(int width, int height, int slice) {
    var mbW = (width + 15) >> 4;
    var mbH = (height + 15) >> 4;
    var grpW = (mbW + 4) / 5;
    var grpH = (mbH + 4) / 5;
    var grpHEdge = grpW * (mbW / grpW);
    var grpVEdge = grpH * (mbH / grpH);
    var grpVRest = mbW - grpHEdge;
    var grpHRest = mbH - grpVEdge;
    var numMbs = mbW * mbH;
    var numTiles = (numMbs + 479) / 480;
    var stdTileBlocks = numMbs / (16 * numTiles);
    var gTile = slice * numTiles;
    var order = new List<(int, int)>();
    for (var tile = 0; tile < numTiles; ++tile, ++gTile) {
      var tileBlocks = stdTileBlocks;
      var tileLimit = -1;
      if (gTile < numMbs - stdTileBlocks * 16 * numTiles) {
        tileLimit = numMbs / (16 * numTiles);
        ++tileBlocks;
      }
      for (var i = 0; i < tileBlocks; ++i) {
        var blkAddr = i == tileLimit ? gTile + 16 * numTiles * i : tile + 16 * numTiles * i + numTiles * HqxTables.Shuffle[(i + slice) & 0xF];
        var locRow = grpH * (blkAddr / (grpH * mbW));
        var locAddr = blkAddr % (grpH * mbW);
        int mbX, pos, mbY;
        if (locRow >= grpVEdge) {
          mbX = grpW * (locAddr / (grpHRest * grpW));
          pos = locAddr % (grpHRest * grpW);
        } else {
          mbX = grpW * (locAddr / (grpH * grpW));
          pos = locAddr % (grpH * grpW);
        }
        if (mbX >= grpHEdge) {
          mbX += pos % grpVRest;
          mbY = locRow + pos / grpVRest;
        } else {
          mbX += pos % grpW;
          mbY = locRow + pos / grpW;
        }
        order.Add((mbX, mbY));
      }
    }
    return order;
  }

  /// <summary>Which block each plane's two vertical halves come from, per layout, as FFmpeg's <c>put_blocks</c> calls are made.</summary>
  internal static (int Plane, int X, int Top, int Bottom, bool Chroma)[] Placement(HqxFormat format) => format switch {
    HqxFormat.Yuv422 => [(0, 0, 0, 2, false), (0, 8, 1, 3, false), (2, 0, 4, 5, true), (1, 0, 6, 7, true)],
    HqxFormat.Yuv444 => [(0, 0, 0, 2, false), (0, 8, 1, 3, false), (2, 0, 4, 6, true), (2, 8, 5, 7, true), (1, 0, 8, 10, true), (1, 8, 9, 11, true)],
    HqxFormat.Yuv422Alpha => [(3, 0, 0, 2, false), (3, 8, 1, 3, false), (0, 0, 4, 6, false), (0, 8, 5, 7, false), (2, 0, 8, 9, true), (1, 0, 10, 11, true)],
    _ => [(3, 0, 0, 2, false), (3, 8, 1, 3, false), (0, 0, 4, 6, false), (0, 8, 5, 7, false), (2, 0, 8, 10, true), (2, 8, 9, 11, true), (1, 0, 12, 14, true), (1, 8, 13, 15, true)],
  };

  internal static int BlockCount(HqxFormat format) => format switch { HqxFormat.Yuv422 => 8, HqxFormat.Yuv444 or HqxFormat.Yuv422Alpha => 12, _ => 16 };

  /// <summary>Whether the DC prediction restarts at this block.</summary>
  internal static bool RestartsDc(HqxFormat format, int block) => format switch {
    HqxFormat.Yuv422 => block is 0 or 4 or 6,
    HqxFormat.Yuv422Alpha => block is 0 or 4 or 8 or 10,
    _ => (block & 3) == 0,
  };

  /// <summary>The planes a layout has: luma, two chroma, and alpha where it has one; and whether its chroma is half width.</summary>
  internal static (int Count, bool HalfChroma) Planes(HqxFormat format) => format switch {
    HqxFormat.Yuv422 => (3, true),
    HqxFormat.Yuv444 => (3, false),
    HqxFormat.Yuv422Alpha => (4, true),
    _ => (4, false),
  };
}

/// <summary>
/// The HQX picture decoder, FFmpeg's <c>libavcodec/hqx.c</c> and <c>hqxdsp.c</c> (LGPL-2.1-or-later).
/// </summary>
internal sealed class HqxFrame {

  internal int Width;
  internal int Height;
  internal HqxFormat Format;
  internal bool Interlaced;
  /// <summary>Planes at the coded size, a multiple of sixteen each way: luma, Cb, Cr, and alpha where there is one.</summary>
  internal ushort[][] Planes = [];
  internal int[] Strides = [];
  internal int CodedHeight;

  internal static HqxFrame Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 8)
      throw new InvalidDataException($"An HQX frame is at least eight bytes; this one is {packet.Length}.");
    var src = packet;
    if (BinaryPrimitives.ReadUInt32LittleEndian(src) == 0x4F464E49) { // "INFO"
      var infoLength = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
      if (infoLength > int.MaxValue || infoLength + 8 > src.Length)
        throw new InvalidDataException("This HQX frame's INFO header runs past its end.");
      src = src[(int)(infoLength + 8)..];
    }
    if (src.Length < HqxShared.HeaderLength)
      throw new InvalidDataException("This HQX frame is shorter than its header.");
    if (src[0] != 'H' || src[1] != 'Q')
      throw new InvalidDataException("This frame does not open with HQ; it is not HQX.");
    var dcbCode = src[3] & 3;
    if (dcbCode == 0)
      throw new InvalidDataException("This HQX frame states 8-bit DC precision, which HQX does not define.");
    var format = src[2] & 7;
    if (format > 3)
      throw new InvalidDataException($"HQX frame layout {format} is not one of the four.");

    var frame = new HqxFrame {
      Interlaced = (src[2] & 0x80) == 0,
      Format = (HqxFormat)format,
      Width = BinaryPrimitives.ReadUInt16BigEndian(src[4..]),
      Height = BinaryPrimitives.ReadUInt16BigEndian(src[6..]),
    };
    if (frame.Width == 0 || frame.Height == 0)
      throw new InvalidDataException("This HQX frame states a picture with no pixels.");
    frame._Allocate();

    var offsets = new int[17];
    for (var i = 0; i < 17; ++i)
      offsets[i] = src[8 + i * 3] << 16 | src[9 + i * 3] << 8 | src[10 + i * 3];
    var dcb = dcbCode + 8;
    var dc = HqxShared.Dc[dcbCode - 1];
    for (var slice = 0; slice < HqxShared.Slices; ++slice) {
      // FFmpeg skips a slice whose offsets are out of order, and draws the rest.
      if (offsets[slice] < HqxShared.HeaderLength || offsets[slice] >= offsets[slice + 1] || offsets[slice + 1] > src.Length)
        continue;
      var reader = new HqxBitReader(src[offsets[slice]..offsets[slice + 1]]);
      foreach (var (x, y) in HqxShared.SliceOrder(frame.Width, frame.Height, slice))
        frame._DecodeMacroblock(ref reader, dc, dcb, x * 16, y * 16);
    }
    return frame;
  }

  private void _Allocate() {
    var codedWidth = (this.Width + 15) & ~15;
    this.CodedHeight = (this.Height + 15) & ~15;
    var (count, half) = HqxShared.Planes(this.Format);
    this.Planes = new ushort[count][];
    this.Strides = new int[count];
    for (var p = 0; p < count; ++p) {
      this.Strides[p] = p is 1 or 2 && half ? codedWidth / 2 : codedWidth;
      this.Planes[p] = new ushort[this.Strides[p] * this.CodedHeight];
    }
  }

  private readonly short[][] _blocks = _NewBlocks();

  private static short[][] _NewBlocks() {
    var blocks = new short[16][];
    for (var i = 0; i < 16; ++i)
      blocks[i] = new short[64];
    return blocks;
  }

  private void _DecodeMacroblock(ref HqxBitReader reader, HqxVlc dc, int dcb, int x, int y) {
    var format = this.Format;
    var count = HqxShared.BlockCount(format);
    var alpha = format is HqxFormat.Yuv422Alpha or HqxFormat.Yuv444Alpha;
    for (var i = 0; i < count; ++i) {
      Array.Clear(this._blocks[i]);
      if (alpha)
        this._blocks[i][0] = -0x800;
    }

    var interlaced = false;
    var coded = (1 << count) - 1;
    var any = true;
    if (alpha) {
      var cbp = HqxShared.ReadCbp(ref reader);
      any = cbp != 0;
      if (format == HqxFormat.Yuv422Alpha) {
        if ((cbp & 0x3) != 0)
          cbp |= 0x500;
        if ((cbp & 0xC) != 0)
          cbp |= 0xA00;
      } else
        cbp |= cbp << 8;
      coded = cbp;
    }
    if (any) {
      if (this.Interlaced)
        interlaced = reader.Read(1) != 0;
      var setting = reader.Read(4);
      var lastDc = 0;
      for (var i = 0; i < count; ++i) {
        if (HqxShared.RestartsDc(format, i))
          lastDc = 0;
        if ((coded & (1 << i)) != 0)
          _DecodeBlock(ref reader, dc, setting, dcb, this._blocks[i], ref lastDc);
      }
    }

    foreach (var (plane, offset, top, bottom, chroma) in HqxShared.Placement(format)) {
      var half = (plane is 1 or 2) && HqxShared.Planes(format).HalfChroma;
      var px = (half ? x >> 1 : x) + offset;
      var quant = chroma ? HqxTables.QuantChroma : HqxTables.QuantLuma;
      var stride = this.Strides[plane];
      var fields = interlaced ? 2 : 1;
      HqxShared.InverseTransform(this._blocks[top], quant, this.Planes[plane], y * stride + px, stride * fields);
      HqxShared.InverseTransform(this._blocks[bottom], quant, this.Planes[plane], (y + (interlaced ? 1 : 8)) * stride + px, stride * fields);
    }
  }

  private static void _DecodeBlock(ref HqxBitReader reader, HqxVlc dc, int setting, int dcb, short[] block, ref int lastDc) {
    lastDc += dc.Read(ref reader);
    // sign_extend(last_dc << (12 - dcb), 12)
    block[0] = (short)((lastDc << (12 - dcb)) << 20 >> 20);
    var q = HqxTables.Quants[setting * 4 + reader.Read(2)];
    var table = HqxShared.Ac[HqxShared.AcIndex(q)];
    var position = 0;
    do {
      var symbol = table.Read(ref reader);
      var level = symbol >> 7;
      var run = symbol & 0x7F;
      position += run;
      if (position > 63)
        break;
      block[HqxShared.ZigZag[position]] = unchecked((short)(level * q));
    } while (position < 63);
  }
}

/// <summary>
/// Writes HQX frames: progressive, 11-bit DC precision, sixteen slices in FFmpeg's macroblock order, and
/// each block at the finest of its macroblock's four quantisers that holds its largest coefficient.
/// </summary>
/// <remarks>
/// The forward transform is a projection onto the basis of HQX's own inverse transform, measured by running
/// it on one coefficient at a time, so what the encoder rounds toward is what the decoder reconstructs.
/// </remarks>
internal sealed class HqxEncoder(int width, int height, HqxFormat format) {

  /// <summary>Quantiser setting 2: 2, 4, 8 and 16 to choose between for each block.</summary>
  private const int _SETTING = 2;
  private const int _DC_CODE = 3;
  private const int _DCB = 11;

  private static readonly double[][] _LumaBasis = _Basis(HqxTables.QuantLuma);
  private static readonly double[][] _ChromaBasis = _Basis(HqxTables.QuantChroma);

  private static double[][] _Basis(byte[] quant) {
    const int probe = 256;
    var basis = new double[64][];
    var block = new short[64];
    var output = new ushort[64];
    for (var k = 0; k < 64; ++k) {
      Array.Clear(block);
      block[k] = probe;
      HqxShared.InverseTransform(block, quant, output, 0, 8);
      basis[k] = new double[64];
      for (var p = 0; p < 64; ++p)
        basis[k][p] = ((output[p] >> 4) - 0x800) / (double)probe;
    }
    return basis;
  }

  /// <summary>One frame from twelve-bit planes at the picture size: luma, Cb, Cr, and alpha where the layout has it.</summary>
  internal byte[] Encode(ushort[][] planes) {
    var codedWidth = (width + 15) & ~15;
    var codedHeight = (height + 15) & ~15;
    var (count, half) = HqxShared.Planes(format);
    var padded = new ushort[count][];
    var strides = new int[count];
    for (var p = 0; p < count; ++p) {
      var w = p is 1 or 2 && half ? (width + 1) / 2 : width;
      strides[p] = p is 1 or 2 && half ? codedWidth / 2 : codedWidth;
      padded[p] = new ushort[strides[p] * codedHeight];
      for (var y = 0; y < codedHeight; ++y)
        for (var x = 0; x < strides[p]; ++x)
          padded[p][y * strides[p] + x] = planes[p][Math.Min(y, height - 1) * w + Math.Min(x, w - 1)];
    }

    var slices = new byte[HqxShared.Slices][];
    for (var s = 0; s < HqxShared.Slices; ++s) {
      var writer = new HqxBitWriter();
      foreach (var (mx, my) in HqxShared.SliceOrder(width, height, s))
        this._Macroblock(writer, padded, strides, mx * 16, my * 16);
      // A small picture leaves some slices no macroblock; FFmpeg complains of one with no bytes, so it gets one.
      slices[s] = writer.ToArray() is { Length: > 0 } data ? data : [0];
    }

    var header = new byte[HqxShared.HeaderLength];
    header[0] = (byte)'H';
    header[1] = (byte)'Q';
    header[2] = (byte)(0x80 | (int)format);
    header[3] = _DC_CODE;
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)height);
    var offset = HqxShared.HeaderLength;
    for (var s = 0; s <= HqxShared.Slices; ++s) {
      header[8 + s * 3] = (byte)(offset >> 16);
      header[9 + s * 3] = (byte)(offset >> 8);
      header[10 + s * 3] = (byte)offset;
      if (s < HqxShared.Slices)
        offset += slices[s].Length;
    }
    var packet = new byte[offset];
    header.CopyTo(packet, 0);
    var at = HqxShared.HeaderLength;
    foreach (var slice in slices) {
      slice.CopyTo(packet, at);
      at += slice.Length;
    }
    return packet;
  }

  private void _Macroblock(HqxBitWriter writer, ushort[][] planes, int[] strides, int x, int y) {
    var alpha = format is HqxFormat.Yuv422Alpha or HqxFormat.Yuv444Alpha;
    if (alpha)
      HqxShared.WriteCbp(writer, 0xF); // every block coded
    writer.Write(_SETTING, 4);

    // Each block's samples, in the block order FFmpeg reads them.
    var count = HqxShared.BlockCount(format);
    var blocks = new (ushort[] Plane, int At, int Stride, bool Chroma)[count];
    foreach (var (plane, offset, top, bottom, chroma) in HqxShared.Placement(format)) {
      var half = (plane is 1 or 2) && HqxShared.Planes(format).HalfChroma;
      var px = (half ? x >> 1 : x) + offset;
      blocks[top] = (planes[plane], y * strides[plane] + px, strides[plane], chroma);
      blocks[bottom] = (planes[plane], (y + 8) * strides[plane] + px, strides[plane], chroma);
    }

    var lastDc = 0;
    Span<double> residual = stackalloc double[64];
    for (var i = 0; i < count; ++i) {
      if (HqxShared.RestartsDc(format, i))
        lastDc = 0;
      var (plane, at, stride, chroma) = blocks[i];
      for (var r = 0; r < 8; ++r)
        for (var c = 0; c < 8; ++c)
          residual[r * 8 + c] = plane[at + r * stride + c] - 0x800;
      var basis = chroma ? _ChromaBasis : _LumaBasis;
      var coefficients = new double[64];
      for (var k = 0; k < 64; ++k) {
        double dot = 0, norm = 0;
        for (var p = 0; p < 64; ++p) {
          dot += residual[p] * basis[k][p];
          norm += basis[k][p] * basis[k][p];
        }
        coefficients[k] = norm > 0 ? dot / norm : 0;
      }

      // DC: the block value is the running DC shifted up by 12 - dcb, so one step is two.
      var dc = Math.Clamp((int)Math.Round(coefficients[0] / 2), -1024, 1023);
      var difference = (dc - lastDc) & ((1 << _DCB) - 1);
      HqxShared.Dc[_DC_CODE - 1].TryWrite(writer, difference);
      lastDc = dc;

      // The finest quantiser of the four that keeps every level within the tables' 255.
      var largest = 0.0;
      for (var k = 1; k < 64; ++k)
        largest = Math.Max(largest, Math.Abs(coefficients[k]));
      var choice = 0;
      while (choice < 3 && largest / HqxTables.Quants[_SETTING * 4 + choice] > 255)
        ++choice;
      var q = HqxTables.Quants[_SETTING * 4 + choice];
      writer.Write(choice, 2);
      _WriteAc(writer, HqxShared.Ac[HqxShared.AcIndex(q)], coefficients, q);
    }
  }

  private static void _WriteAc(HqxBitWriter writer, HqxVlc table, double[] coefficients, int q) {
    var position = 0;
    for (var z = 1; z < 64; ++z) {
      var level = Math.Clamp((int)Math.Round(coefficients[HqxShared.ZigZag[z]] / q), -255, 255);
      if (level == 0)
        continue;
      var gap = z - position;
      if (!table.TryWrite(writer, level * 128 + gap)) {
        // Step over the zeros with level-zero codes, then land on the coefficient.
        var zeros = gap - 1;
        while (zeros > 0) {
          var step = zeros;
          while (step > 0 && !table.TryWrite(writer, step))
            --step;
          if (step == 0)
            throw new InvalidOperationException("No HQX code steps over a single zero.");
          zeros -= step;
        }
        while (level != 0 && !table.TryWrite(writer, level * 128 + 1))
          level -= Math.Sign(level);
        if (level == 0)
          table.TryWrite(writer, 1);
      }
      position = z;
    }
    // A run past the last coefficient ends the block.
    if (position < 63)
      for (var run = 65; run > 63 - position; --run)
        if (table.TryWrite(writer, run))
          return;
  }
}
