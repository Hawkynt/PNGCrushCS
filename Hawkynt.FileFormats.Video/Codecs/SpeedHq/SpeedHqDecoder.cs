using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Dv;

namespace FileFormat.Codecs.SpeedHq;

/// <summary>A least-significant-bit-first reader, as SpeedHQ's slices are written; reads past the end return zeros.</summary>
internal struct SpeedHqBitReader(byte[] data, int start, int length) {
  private readonly byte[] _data = data;
  private readonly int _end = start + length;
  private long _position = (long)start * 8;

  public readonly long BitsLeft => (long)this._end * 8 - this._position;

  public int ReadBit() {
    var at = this._position >> 3;
    var bit = at < this._end ? (this._data[at] >> (int)(this._position & 7)) & 1 : 0;
    ++this._position;
    return bit;
  }

  /// <summary>FFmpeg's <c>get_bits_le</c>: the first bit read is bit 0 of the value.</summary>
  public int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value |= this.ReadBit() << i;
    return value;
  }
}

/// <summary>SpeedHQ's planes and its chroma and alpha forms, by FourCC.</summary>
internal enum SpeedHqSubsampling { Yuv420, Yuv422, Yuv444 }

internal enum SpeedHqAlpha { None, RunLength, Dct }

/// <summary>
/// NewTek SpeedHQ: intra-only MPEG-2-style DCT pictures in four interleaved slices per field, read
/// least significant bit first.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/speedhqdec.c</c> (Steinar H. Gunderson), LGPL-2.1-or-later;
/// see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>. This is the codec <c>codec-investigations.md</c> recorded as
/// reassigning MPEG-2 AC codewords that only FFmpeg printed: the reassigned table is FFmpeg's
/// <c>speedhq.c</c>, copied exactly. The inverse transform is FFmpeg's <c>simple_idct</c>, which
/// <see cref="DvInverseDct"/> already carries bit for bit.
/// </remarks>
internal sealed class SpeedHqDecoder {

  private static readonly Dictionary<(int, int), int> _Ac = _LittleEndianCodes(SpeedHqTables.AcCodes);
  private static readonly Dictionary<(int, int), int> _DcLuma = _BigEndianCodes(SpeedHqTables.DcLumaCode, SpeedHqTables.DcLumaBits);
  private static readonly Dictionary<(int, int), int> _DcChroma = _BigEndianCodes(SpeedHqTables.DcChromaCode, SpeedHqTables.DcChromaBits);
  private static readonly (Dictionary<(int, int), int> Codes, int[] Symbols) _AlphaRun = _AlphaRunCodes();
  private static readonly (Dictionary<(int, int), int> Codes, int[] Symbols) _AlphaLevel = _AlphaLevelCodes();

  private static Dictionary<(int, int), int> _LittleEndianCodes(ReadOnlySpan<(int Code, int Length)> table) {
    var codes = new Dictionary<(int, int), int>();
    for (var i = 0; i < table.Length; ++i)
      codes[(table[i].Length, table[i].Code)] = i;
    return codes;
  }

  private static Dictionary<(int, int), int> _BigEndianCodes(ushort[] code, byte[] bits) {
    var codes = new Dictionary<(int, int), int>();
    for (var i = 0; i < code.Length; ++i)
      codes[(bits[i], code[i])] = i;
    return codes;
  }

  // FFmpeg's compute_alpha_vlcs: codes as written for a little-endian reader.
  private static (Dictionary<(int, int), int>, int[]) _AlphaRunCodes() {
    var codes = new Dictionary<(int, int), int>();
    var symbols = new List<int>();
    void Add(int code, int length, int symbol) {
      codes[(length, code)] = symbols.Count;
      symbols.Add(symbol);
    }
    Add(0, 1, 0);
    for (var i = 0; i < 4; ++i)
      Add((i << 2) | 1, 4, i + 1);
    for (var i = 0; i < 128; ++i)
      Add((i << 3) | 7, 10, i);
    Add(3, 3, -1);
    return (codes, symbols.ToArray());
  }

  private static (Dictionary<(int, int), int>, int[]) _AlphaLevelCodes() {
    var codes = new Dictionary<(int, int), int>();
    var symbols = new List<int>();
    void Add(int code, int length, int symbol) {
      codes[(length, code)] = symbols.Count;
      symbols.Add(symbol);
    }
    for (var sign = 0; sign <= 1; ++sign) {
      Add((sign << 1) | 1, 2, sign != 0 ? -1 : 1);
      for (var i = 0; i < 4; ++i)
        Add((i << 3) | (sign << 2) | 2, 5, sign != 0 ? -(i + 2) : i + 2);
    }
    for (var i = 0; i < 256; ++i)
      Add(i << 2, 10, i);
    return (codes, symbols.ToArray());
  }

  internal static int ReadLittleEndianCode(ref SpeedHqBitReader bits, Dictionary<(int, int), int> codes, int maxLength) {
    var code = 0;
    for (var length = 1; length <= maxLength; ++length) {
      code |= bits.ReadBit() << (length - 1);
      if (codes.TryGetValue((length, code), out var symbol))
        return symbol;
    }
    throw new InvalidDataException("A SpeedHQ code names nothing.");
  }

  private static int _ReadBigEndianCode(ref SpeedHqBitReader bits, Dictionary<(int, int), int> codes, int maxLength) {
    var code = 0;
    for (var length = 1; length <= maxLength; ++length) {
      code = (code << 1) | bits.ReadBit();
      if (codes.TryGetValue((length, code), out var symbol))
        return symbol;
    }
    throw new InvalidDataException("A SpeedHQ DC size code names nothing.");
  }

  /// <summary>Planes at a size every macroblock the format may write — including past the picture — fits into.</summary>
  internal sealed class Picture {
    public readonly int Width;
    public readonly int Height;
    public readonly int ChromaWidth;
    public readonly int ChromaHeight;
    public readonly byte[] Y;
    public readonly byte[] U;
    public readonly byte[] V;
    public readonly byte[]? A;

    public Picture(int width, int height, SpeedHqSubsampling subsampling, bool alpha) {
      this.Width = ((width + 15) & ~15) + 16;
      this.Height = ((height + 31) & ~31) + 32;
      this.ChromaWidth = subsampling == SpeedHqSubsampling.Yuv444 ? this.Width : this.Width / 2;
      this.ChromaHeight = subsampling == SpeedHqSubsampling.Yuv420 ? this.Height / 2 : this.Height;
      this.Y = new byte[this.Width * this.Height];
      this.U = new byte[this.ChromaWidth * this.ChromaHeight];
      this.V = new byte[this.ChromaWidth * this.ChromaHeight];
      this.A = alpha ? new byte[this.Width * this.Height] : null;
    }
  }

  private readonly int _width;
  private readonly int _height;
  private readonly SpeedHqSubsampling _subsampling;
  private readonly SpeedHqAlpha _alpha;
  private readonly int[] _quant = new int[64];

  internal SpeedHqDecoder(int width, int height, SpeedHqSubsampling subsampling, SpeedHqAlpha alpha) {
    this._width = width;
    this._height = height;
    this._subsampling = subsampling;
    this._alpha = alpha;
  }

  internal Picture Decode(byte[] data) {
    if (data.Length < 4 || this._width < 8 || this._width % 8 != 0)
      throw new InvalidDataException("A SpeedHQ frame is too short, or its picture is not a multiple of eight wide.");
    if (data.Length < this._width * this._height / 64 / 4)
      throw new InvalidDataException("A SpeedHQ frame is too short for its picture.");

    var quality = data[0];
    if (quality >= 100)
      throw new InvalidDataException($"A SpeedHQ frame states the quality {quality}, which is above 99.");
    for (var i = 0; i < 64; ++i)
      this._quant[i] = SpeedHqTables.UnscaledQuant[SpeedHqTables.Zigzag[i]] * (100 - quality);

    var secondField = data[1] | (data[2] << 8) | (data[3] << 16);
    if (secondField >= data.Length - 3)
      throw new InvalidDataException("A SpeedHQ frame's second field starts past its end.");

    var picture = new Picture(this._width, this._height, this._subsampling, this._alpha != SpeedHqAlpha.None);

    // A second field that overlaps the first, at either end, means the frame codes one field only.
    if (secondField == 4 || secondField == data.Length - 4) {
      for (var slice = 0; slice < 4; ++slice)
        this._DecodeField(data, picture, 0, 4, data.Length, 1, slice);
    } else {
      for (var slice = 0; slice < 4; ++slice)
        this._DecodeField(data, picture, 0, 4, secondField, 2, slice);
      for (var slice = 0; slice < 4; ++slice)
        this._DecodeField(data, picture, 1, secondField, data.Length, 2, slice);
    }
    return picture;
  }

  private void _DecodeField(byte[] data, Picture picture, int field, int start, int end, int lineStride, int slice) {
    if (end < start || end - start < 3 || end > data.Length)
      throw new InvalidDataException("A SpeedHQ field lies outside its frame.");

    Span<int> offsets = stackalloc int[5];
    offsets[0] = start;
    offsets[4] = end;
    for (var x = 1; x < 4; ++x) {
      var last = offsets[x - 1];
      var length = data[last] | (data[last + 1] << 8) | (data[last + 2] << 16);
      offsets[x] = last + length;
      if (length < 3 || offsets[x] > end - 3)
        throw new InvalidDataException("A SpeedHQ slice length runs past its field.");
    }

    var bits = new SpeedHqBitReader(data, offsets[slice] + 3, offsets[slice + 1] - offsets[slice] - 3);
    Span<int> lastDc = stackalloc int[4];
    Span<byte> lastAlpha = stackalloc byte[16];

    for (var y = slice * 16 * lineStride; y < this._height; y += lineStride * 64) {
      lastDc.Fill(1024);
      lastAlpha.Fill(255);
      var columns = this._width - 8 * (this._subsampling != SpeedHqSubsampling.Yuv444 ? 1 : 0);
      for (var x = 0; x < columns; x += 16)
        this._DecodeMacroblock(ref bits, picture, field, lineStride, x, y, lastDc, lastAlpha);
    }

    // A width that is a multiple of eight but not sixteen leaves a last half macroblock column, which
    // the last slice codes after its own rows, across every row of the field.
    if (this._subsampling != SpeedHqSubsampling.Yuv444 && (this._width & 15) != 0 && slice == 3) {
      for (var y = 0; y < this._height; y += 16 * lineStride) {
        lastDc.Fill(1024);
        lastAlpha.Fill(255);
        this._DecodeMacroblock(ref bits, picture, field, lineStride, this._width - 8, y, lastDc, lastAlpha);
      }
    }
  }

  private void _DecodeMacroblock(
    ref SpeedHqBitReader bits, Picture p, int field, int lineStride, int x, int y, Span<int> lastDc, Span<byte> lastAlpha) {
    var ys = p.Width * lineStride;
    var yAt = p.Width * (y + field) + x;
    var cs = p.ChromaWidth * lineStride;
    var chromaRow = this._subsampling == SpeedHqSubsampling.Yuv420 ? y / 2 : y;
    var chromaX = this._subsampling == SpeedHqSubsampling.Yuv444 ? x : x / 2;
    var cAt = p.ChromaWidth * (chromaRow + field) + chromaX;

    this._Block(ref bits, lastDc, 0, p.Y, yAt, ys);
    this._Block(ref bits, lastDc, 0, p.Y, yAt + 8, ys);
    this._Block(ref bits, lastDc, 0, p.Y, yAt + 8 * ys, ys);
    this._Block(ref bits, lastDc, 0, p.Y, yAt + 8 * ys + 8, ys);
    this._Block(ref bits, lastDc, 1, p.U, cAt, cs);
    this._Block(ref bits, lastDc, 2, p.V, cAt, cs);
    if (this._subsampling != SpeedHqSubsampling.Yuv420) {
      this._Block(ref bits, lastDc, 1, p.U, cAt + 8 * cs, cs);
      this._Block(ref bits, lastDc, 2, p.V, cAt + 8 * cs, cs);
      if (this._subsampling == SpeedHqSubsampling.Yuv444) {
        this._Block(ref bits, lastDc, 1, p.U, cAt + 8, cs);
        this._Block(ref bits, lastDc, 2, p.V, cAt + 8, cs);
        this._Block(ref bits, lastDc, 1, p.U, cAt + 8 * cs + 8, cs);
        this._Block(ref bits, lastDc, 2, p.V, cAt + 8 * cs + 8, cs);
      }
    }

    if (this._alpha == SpeedHqAlpha.RunLength) {
      _AlphaBlock(ref bits, lastAlpha, p.A!, yAt, ys);
      _AlphaBlock(ref bits, lastAlpha, p.A!, yAt + 8 * ys, ys);
    } else if (this._alpha == SpeedHqAlpha.Dct) {
      this._Block(ref bits, lastDc, 3, p.A!, yAt, ys);
      this._Block(ref bits, lastDc, 3, p.A!, yAt + 8, ys);
      this._Block(ref bits, lastDc, 3, p.A!, yAt + 8 * ys, ys);
      this._Block(ref bits, lastDc, 3, p.A!, yAt + 8 * ys + 8, ys);
    }
  }

  private void _Block(ref SpeedHqBitReader bits, Span<int> lastDc, int component, byte[] plane, int at, int stride) {
    Span<short> block = stackalloc short[64];

    var luma = component is 0 or 3;
    var size = _ReadBigEndianCode(ref bits, luma ? _DcLuma : _DcChroma, luma ? 9 : 10);
    var diff = 0;
    if (size != 0) {
      diff = bits.ReadBits(size);
      if (((diff >> (size - 1)) & 1) == 0)
        diff -= (1 << size) - 1;
    }
    lastDc[component] -= diff; // opposite of most codecs
    block[0] = (short)lastDc[component];

    var i = 0;
    for (;;) {
      var symbol = ReadLittleEndianCode(ref bits, _Ac, 16);
      int level, run;
      if (symbol == SpeedHqTables.EndOfBlock)
        break;
      if (symbol == SpeedHqTables.Escape) {
        run = bits.ReadBits(6) + 1;
        level = bits.ReadBits(12) - 2048;
      } else {
        run = SpeedHqTables.AcRun[symbol] + 1;
        level = SpeedHqTables.AcLevel[symbol];
        if (bits.ReadBit() != 0)
          level = -level;
      }

      i += run;
      if (i > 63)
        throw new InvalidDataException("A SpeedHQ block runs past its sixty-fourth coefficient.");
      block[SpeedHqTables.Zigzag[i]] = unchecked((short)((level * this._quant[i]) >> 4));
    }

    DvInverseDct.Put(block, plane, at, stride);
  }

  private static void _AlphaBlock(ref SpeedHqBitReader bits, Span<byte> lastAlpha, byte[] plane, int at, int stride) {
    Span<byte> block = stackalloc byte[128];
    var i = 0;
    for (;;) {
      var run = _AlphaRun.Symbols[ReadLittleEndianCode(ref bits, _AlphaRun.Codes, 10)];
      if (run < 0)
        break;
      i += run;
      if (i >= 128)
        throw new InvalidDataException("A SpeedHQ alpha block runs past its 128 samples.");
      block[i++] = unchecked((byte)_AlphaLevel.Symbols[ReadLittleEndianCode(ref bits, _AlphaLevel.Codes, 10)]);
    }

    for (var y = 0; y < 8; ++y) {
      for (var x = 0; x < 16; ++x)
        lastAlpha[x] = unchecked((byte)(lastAlpha[x] - block[y * 16 + x]));
      lastAlpha.CopyTo(plane.AsSpan(at + y * stride, 16));
    }
  }
}
