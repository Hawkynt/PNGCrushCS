using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg;

namespace FileFormat.Codecs.SpeedHq;

/// <summary>A writer for SpeedHQ's least-significant-bit-first slices.</summary>
internal sealed class SpeedHqBitWriter {
  private readonly List<byte> _bytes = [];
  private int _bit;
  private int _current;

  /// <summary>Writes <paramref name="value"/>'s low bits, bit 0 first: FFmpeg's <c>put_bits_le</c>.</summary>
  public void WriteLsbFirst(int count, uint value) {
    for (var i = 0; i < count; ++i)
      this._Bit((int)((value >> i) & 1));
  }

  /// <summary>Writes a code most significant bit first, as MPEG-2's DC codes are read.</summary>
  public void WriteMsbFirst(int count, uint value) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((int)((value >> i) & 1));
  }

  private void _Bit(int bit) {
    this._current |= bit << this._bit;
    if (++this._bit == 8) {
      this._bytes.Add((byte)this._current);
      this._current = 0;
      this._bit = 0;
    }
  }

  /// <summary>The bytes written, the last one padded with zero bits.</summary>
  public byte[] ToBytes() {
    var result = new List<byte>(this._bytes);
    if (this._bit != 0)
      result.Add((byte)this._current);
    return result.ToArray();
  }
}

/// <summary>
/// Encodes NewTek SpeedHQ progressive frames in any of its eight forms.
/// </summary>
/// <remarks>
/// The bitstream elements — DC coding as differences from the last DC upwards, AC run-level codes with
/// a 24-bit escape, the end-of-block code, four slices with 24-bit little-endian lengths — follow
/// FFmpeg's <c>libavcodec/speedhqenc.c</c> (LGPL-2.1-or-later), whose functions <c>encode_dc</c> and
/// <c>encode_block</c> they mirror. The rest is this package's own: FFmpeg's encoder writes only the
/// three alpha-less forms at widths that are multiples of sixteen, through its MPEG encoder, while this
/// one also writes the half-macroblock border column of a width that is a multiple of eight, run-length
/// alpha and DCT alpha. SpeedHQ is intra-only and lossy; every picture is a key frame.
/// </remarks>
internal sealed class SpeedHqEncoder {

  private static readonly Dictionary<(int Run, int Level), int> _Symbols = _BuildSymbols();

  private static Dictionary<(int, int), int> _BuildSymbols() {
    var map = new Dictionary<(int, int), int>();
    for (var i = 0; i < SpeedHqTables.AcLevel.Length; ++i)
      map[(SpeedHqTables.AcRun[i], SpeedHqTables.AcLevel[i])] = i;
    return map;
  }

  private readonly int _width;
  private readonly int _height;
  private readonly SpeedHqSubsampling _subsampling;
  private readonly SpeedHqAlpha _alpha;
  private readonly int _scale;
  private readonly int[] _quant = new int[64];

  internal SpeedHqEncoder(int width, int height, SpeedHqSubsampling subsampling, SpeedHqAlpha alpha, int scale) {
    this._width = width;
    this._height = height;
    this._subsampling = subsampling;
    this._alpha = alpha;
    this._scale = scale;
    for (var i = 0; i < 64; ++i)
      this._quant[i] = SpeedHqTables.UnscaledQuant[SpeedHqTables.Zigzag[i]] * scale;
  }

  /// <summary>Codes one picture from planes at the picture's own size, chroma at its subsampled size.</summary>
  internal byte[] Encode(byte[] y, byte[] u, byte[] v, int chromaWidth, int chromaHeight, byte[]? alpha) {
    var slices = new[] { new SpeedHqBitWriter(), new SpeedHqBitWriter(), new SpeedHqBitWriter(), new SpeedHqBitWriter() };
    Span<int> lastDc = stackalloc int[4];
    Span<byte> lastAlpha = stackalloc byte[16];

    var columns = this._width - 8 * (this._subsampling != SpeedHqSubsampling.Yuv444 ? 1 : 0);
    for (var row = 0; row * 16 < this._height; ++row) {
      var writer = slices[row % 4];
      lastDc.Fill(1024);
      lastAlpha.Fill(255);
      for (var x = 0; x < columns; x += 16)
        this._Macroblock(writer, y, u, v, chromaWidth, chromaHeight, alpha, x, row * 16, lastDc, lastAlpha);
    }

    if (this._subsampling != SpeedHqSubsampling.Yuv444 && (this._width & 15) != 0)
      for (var top = 0; top < this._height; top += 16) {
        lastDc.Fill(1024);
        lastAlpha.Fill(255);
        this._Macroblock(slices[3], y, u, v, chromaWidth, chromaHeight, alpha, this._width - 8, top, lastDc, lastAlpha);
      }

    var output = new List<byte> { (byte)(100 - this._scale), 4, 0, 0 };
    foreach (var slice in slices) {
      var bytes = slice.ToBytes();
      var length = bytes.Length + 3;
      output.Add((byte)length);
      output.Add((byte)(length >> 8));
      output.Add((byte)(length >> 16));
      output.AddRange(bytes);
    }
    return output.ToArray();
  }

  private void _Macroblock(
    SpeedHqBitWriter writer, byte[] y, byte[] u, byte[] v, int chromaWidth, int chromaHeight, byte[]? alpha,
    int x, int top, Span<int> lastDc, Span<byte> lastAlpha) {
    this._Block(writer, y, this._width, this._height, x, top, 0, lastDc);
    this._Block(writer, y, this._width, this._height, x + 8, top, 0, lastDc);
    this._Block(writer, y, this._width, this._height, x, top + 8, 0, lastDc);
    this._Block(writer, y, this._width, this._height, x + 8, top + 8, 0, lastDc);

    var chromaTop = this._subsampling == SpeedHqSubsampling.Yuv420 ? top / 2 : top;
    var chromaX = this._subsampling == SpeedHqSubsampling.Yuv444 ? x : x / 2;
    this._Block(writer, u, chromaWidth, chromaHeight, chromaX, chromaTop, 1, lastDc);
    this._Block(writer, v, chromaWidth, chromaHeight, chromaX, chromaTop, 2, lastDc);
    if (this._subsampling != SpeedHqSubsampling.Yuv420) {
      this._Block(writer, u, chromaWidth, chromaHeight, chromaX, chromaTop + 8, 1, lastDc);
      this._Block(writer, v, chromaWidth, chromaHeight, chromaX, chromaTop + 8, 2, lastDc);
      if (this._subsampling == SpeedHqSubsampling.Yuv444) {
        this._Block(writer, u, chromaWidth, chromaHeight, chromaX + 8, chromaTop, 1, lastDc);
        this._Block(writer, v, chromaWidth, chromaHeight, chromaX + 8, chromaTop, 2, lastDc);
        this._Block(writer, u, chromaWidth, chromaHeight, chromaX + 8, chromaTop + 8, 1, lastDc);
        this._Block(writer, v, chromaWidth, chromaHeight, chromaX + 8, chromaTop + 8, 2, lastDc);
      }
    }

    if (this._alpha == SpeedHqAlpha.RunLength) {
      this._AlphaBlock(writer, alpha!, x, top, lastAlpha);
      this._AlphaBlock(writer, alpha!, x, top + 8, lastAlpha);
    } else if (this._alpha == SpeedHqAlpha.Dct) {
      this._Block(writer, alpha!, this._width, this._height, x, top, 3, lastDc);
      this._Block(writer, alpha!, this._width, this._height, x + 8, top, 3, lastDc);
      this._Block(writer, alpha!, this._width, this._height, x, top + 8, 3, lastDc);
      this._Block(writer, alpha!, this._width, this._height, x + 8, top + 8, 3, lastDc);
    }
  }

  private static int _Sample(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Min(y, height - 1) * width + Math.Min(x, width - 1)];

  private void _Block(SpeedHqBitWriter writer, byte[] plane, int width, int height, int x0, int y0, int component, Span<int> lastDc) {
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    for (var r = 0; r < 8; ++r)
    for (var c = 0; c < 8; ++c)
      samples[r * 8 + c] = _Sample(plane, width, height, x0 + c, y0 + r);
    MpegForwardDct.Transform(samples, coefficients);

    // DC, coded as how far the last one is above it.
    var dc = Math.Clamp((int)Math.Round(coefficients[0]), 0, 2040);
    _WriteDc(writer, lastDc[component] - dc, component is 0 or 3);
    lastDc[component] = dc;

    var last = 0;
    for (var i = 1; i < 64; ++i) {
      var level = (int)Math.Round(coefficients[SpeedHqTables.Zigzag[i]] * 16 / this._quant[i]);
      if (level == 0)
        continue;
      level = Math.Clamp(level, -2048, 2047);
      var run = i - last - 1;
      var magnitude = Math.Abs(level);
      if (_Symbols.TryGetValue((run, magnitude), out var symbol)) {
        var (code, length) = SpeedHqTables.AcCodes[symbol];
        writer.WriteLsbFirst(length, (uint)code);
        writer.WriteLsbFirst(1, level < 0 ? 1u : 0u);
      } else {
        writer.WriteLsbFirst(24, 0x20u | (uint)run << 6 | (uint)(level + 2048) << 12);
      }
      last = i;
    }

    writer.WriteLsbFirst(4, 6);
  }

  private static void _WriteDc(SpeedHqBitWriter writer, int diff, bool luma) {
    var magnitude = Math.Abs(diff);
    var size = 0;
    while ((1 << size) <= magnitude)
      ++size;

    var code = luma ? SpeedHqTables.DcLumaCode[size] : SpeedHqTables.DcChromaCode[size];
    var bits = luma ? SpeedHqTables.DcLumaBits[size] : SpeedHqTables.DcChromaBits[size];
    writer.WriteMsbFirst(bits, code);
    if (size != 0)
      writer.WriteLsbFirst(size, (uint)((diff < 0 ? diff - 1 : diff) & ((1 << size) - 1)));
  }

  /// <summary>A 16x8 alpha block as the difference from the row above, run-length coded.</summary>
  private void _AlphaBlock(SpeedHqBitWriter writer, byte[] alpha, int x0, int y0, Span<byte> lastAlpha) {
    Span<byte> block = stackalloc byte[128];
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 16; ++x) {
      var value = (byte)_Sample(alpha, this._width, this._height, x0 + x, y0 + y);
      block[y * 16 + x] = unchecked((byte)(lastAlpha[x] - value));
      lastAlpha[x] = value;
    }

    var run = 0;
    for (var i = 0; i < 128; ++i) {
      if (block[i] == 0) {
        ++run;
        continue;
      }

      _WriteAlphaRun(writer, run);
      var level = (sbyte)block[i];
      if (level is 1 or -1) {
        writer.WriteLsbFirst(2, (uint)(((level < 0 ? 1 : 0) << 1) | 1));
      } else if (Math.Abs((int)level) is >= 2 and <= 5) {
        writer.WriteLsbFirst(5, (uint)(((Math.Abs((int)level) - 2) << 3) | ((level < 0 ? 1 : 0) << 2) | 2));
      } else {
        writer.WriteLsbFirst(10, (uint)block[i] << 2);
      }
      run = 0;
    }

    writer.WriteLsbFirst(3, 3); // end of block
  }

  private static void _WriteAlphaRun(SpeedHqBitWriter writer, int run) {
    if (run == 0)
      writer.WriteLsbFirst(1, 0);
    else if (run <= 4)
      writer.WriteLsbFirst(4, (uint)(((run - 1) << 2) | 1));
    else
      writer.WriteLsbFirst(10, (uint)((run << 3) | 7));
  }
}
