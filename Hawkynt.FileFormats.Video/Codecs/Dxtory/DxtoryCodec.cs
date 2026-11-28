using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Dxtory;

/// <summary>The sample layouts a Dxtory frame header names.</summary>
internal enum DxtoryLayout { Bgr24, Yuv420, Yuv410, Yuv444, Rgb565, Rgb555 }

/// <summary>
/// One plane with room around it: FFmpeg's decoder writes a sample or two past the right edge, and a row
/// past the bottom, in some odd-sized layouts, into the padding of its own buffers; here that lands in the
/// margin and is never shown.
/// </summary>
internal sealed class DxtoryPlane {
  private const int _MARGIN = 16;

  internal readonly int Width;
  internal readonly int Height;
  internal readonly int Stride;
  internal readonly byte[] Data;

  internal DxtoryPlane(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Stride = width + _MARGIN;
    this.Data = new byte[this.Stride * (height + _MARGIN)];
  }

  internal byte this[int x, int y] {
    get => (uint)x < (uint)this.Stride && (uint)y < (uint)(this.Height + _MARGIN) ? this.Data[y * this.Stride + x] : (byte)0;
    set {
      if ((uint)x < (uint)this.Stride && (uint)y < (uint)(this.Height + _MARGIN))
        this.Data[y * this.Stride + x] = value;
    }
  }

  /// <summary>The visible samples, rows reversed where the frame is stored bottom up.</summary>
  internal void CopyTo(Span<byte> destination, bool flipped) {
    for (var y = 0; y < this.Height; ++y)
      this.Data.AsSpan((flipped ? this.Height - 1 - y : y) * this.Stride, this.Width).CopyTo(destination[(y * this.Width)..]);
  }

  /// <summary>Fills the visible samples from a top-down plane.</summary>
  internal void CopyFrom(ReadOnlySpan<byte> source) {
    for (var y = 0; y < this.Height; ++y)
      source.Slice(y * this.Width, this.Width).CopyTo(this.Data.AsSpan(y * this.Stride));
  }
}

/// <summary>
/// Dxtory's version 2 symbols: a move-to-front list of eight recent values a channel, a run of ones
/// naming a list entry and a zero followed by eight bits naming a new one, read least-significant bit first.
/// One object reads them when decoding and writes them when encoding, so both walk the picture alike.
/// </summary>
internal sealed class DxtorySymbols {

  internal static readonly byte[] DefaultList = [0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0, 0xFF];
  internal static readonly byte[] DefaultList555 = [0x00, 0x08, 0x10, 0x18, 0x1F, 0, 0, 0];
  internal static readonly byte[] DefaultList565 = [0x00, 0x08, 0x10, 0x20, 0x30, 0x3F, 0, 0];

  private readonly byte[] _data;
  private readonly int _bits;
  private int _position;
  private readonly List<byte>? _output;
  private int _current;
  private int _filled;
  private readonly byte[][] _lists = [new byte[8], new byte[8], new byte[8]];

  /// <summary>Reading a slice.</summary>
  internal DxtorySymbols(ReadOnlySpan<byte> slice) {
    this._data = slice.ToArray();
    this._bits = this._data.Length * 8;
  }

  /// <summary>Writing one.</summary>
  internal DxtorySymbols() {
    this._data = [];
    this._output = [];
  }

  internal bool Writing => this._output != null;

  /// <summary>Bits left to read; a writer always has room.</summary>
  internal int BitsLeft => this.Writing ? int.MaxValue : this._bits - this._position;

  internal void Reset(byte[] first, byte[] second, byte[] third) {
    first.CopyTo(this._lists[0], 0);
    second.CopyTo(this._lists[1], 0);
    third.CopyTo(this._lists[2], 0);
  }

  private int _Bit() {
    if (this._position >= this._bits) {
      ++this._position;
      return 0;
    }
    var bit = (this._data[this._position >> 3] >> (this._position & 7)) & 1;
    ++this._position;
    return bit;
  }

  private void _Put(int bit) {
    this._current |= bit << this._filled;
    if (++this._filled == 8) {
      this._output!.Add((byte)this._current);
      this._current = 0;
      this._filled = 0;
    }
  }

  /// <summary>
  /// FFmpeg's <c>decode_sym</c> and <c>decode_sym_565</c>: the list entry a run of ones names, or a new
  /// value of <paramref name="bits"/> bits after a zero; the value moves to the front of the list. When
  /// writing, <paramref name="value"/> is coded and returned.
  /// </summary>
  internal byte Symbol(int channel, byte value, int bits = 8) {
    var list = this._lists[channel];
    // The run is at most eight long for eight-bit values, and as long as the sample's bits otherwise.
    var longest = bits;
    if (this.Writing) {
      var found = -1;
      for (var i = 0; i < longest; ++i)
        if (list[i] == value) {
          found = i;
          break;
        }
      if (found >= 0) {
        for (var i = 0; i <= found; ++i)
          this._Put(1);
        if (found + 1 < longest)
          this._Put(0);
        _MoveToFront(list, found, value);
      } else {
        this._Put(0);
        for (var i = 0; i < bits; ++i)
          this._Put((value >> i) & 1);
        _Insert(list, value, bits == 8 ? 8 : 6);
      }
      return value;
    }

    var run = 0;
    while (run < longest && this._Bit() != 0)
      ++run;
    byte result;
    if (run == 0) {
      var v = 0;
      for (var i = 0; i < bits; ++i)
        v |= this._Bit() << i;
      result = (byte)v;
      _Insert(list, result, bits == 8 ? 8 : 6);
    } else {
      result = list[run - 1];
      _MoveToFront(list, run - 1, result);
    }
    return result;
  }

  private static void _MoveToFront(byte[] list, int index, byte value) {
    for (var i = index; i > 0; --i)
      list[i] = list[i - 1];
    list[0] = value;
  }

  /// <summary>A new value pushes the list along; FFmpeg's 5- and 6-bit lists move only their first six.</summary>
  private static void _Insert(byte[] list, byte value, int moved) {
    for (var i = moved - 1; i > 0; --i)
      list[i] = list[i - 1];
    list[0] = value;
  }

  internal byte[] ToArray() {
    var result = new List<byte>(this._output!);
    if (this._filled != 0)
      result.Add((byte)this._current);
    return [.. result];
  }
}

/// <summary>
/// The Dxtory picture decoder and the walk both directions share, FFmpeg's <c>libavcodec/dxtory.c</c>
/// (LGPL-2.1-or-later).
/// </summary>
internal sealed class DxtoryFrame {

  internal const int HeaderLength = 16;

  internal readonly int Width;
  internal readonly int Height;
  internal readonly DxtoryLayout Layout;
  internal readonly DxtoryPlane[] Planes;

  internal DxtoryFrame(int width, int height, DxtoryLayout layout) {
    this.Width = width;
    this.Height = height;
    this.Layout = layout;
    this.Planes = layout switch {
      DxtoryLayout.Yuv420 => [new(width, height), new((width + 1) >> 1, (height + 1) >> 1), new((width + 1) >> 1, (height + 1) >> 1)],
      DxtoryLayout.Yuv410 => [new(width, height), new((width + 3) >> 2, (height + 3) >> 2), new((width + 3) >> 2, (height + 3) >> 2)],
      DxtoryLayout.Yuv444 => [new(width, height), new(width, height), new(width, height)],
      DxtoryLayout.Rgb565 or DxtoryLayout.Rgb555 => [new(width * 2, height)],
      _ => [new(width * 3, height)],
    };
  }

  /// <summary>The frame types FFmpeg reads: layout, and whether it is version 2. Bit 0x20 flips the picture.</summary>
  internal static (DxtoryLayout Layout, bool Coded)? Classify(uint type) => (type & ~0x20u) switch {
    0x01000001 => (DxtoryLayout.Bgr24, false),
    0x01000009 => (DxtoryLayout.Bgr24, true),
    0x02000001 => (DxtoryLayout.Yuv420, false),
    0x02000009 => (DxtoryLayout.Yuv420, true),
    0x03000001 => (DxtoryLayout.Yuv410, false),
    0x03000009 => (DxtoryLayout.Yuv410, true),
    0x04000001 => (DxtoryLayout.Yuv444, false),
    0x04000009 => (DxtoryLayout.Yuv444, true),
    0x17000001 => (DxtoryLayout.Rgb565, false),
    0x17000009 => (DxtoryLayout.Rgb565, true),
    0x18000001 or 0x19000001 => (DxtoryLayout.Rgb555, false),
    0x18000009 or 0x19000009 => (DxtoryLayout.Rgb555, true),
    _ => null,
  };

  /// <summary>Decodes one packet into a picture.</summary>
  internal static RawImage Decode(ReadOnlySpan<byte> packet, int width, int height) {
    if (packet.Length < HeaderLength)
      throw new InvalidDataException($"A Dxtory frame opens with a {HeaderLength}-byte header; this packet is {packet.Length} bytes.");
    var type = BinaryPrimitives.ReadUInt32BigEndian(packet);
    if (Classify(type) is not var (layout, coded))
      throw new NotSupportedException($"Dxtory frame type 0x{type:X8} is not one FFmpeg reads either.");
    var flipped = (type & 0x20) != 0;
    var frame = new DxtoryFrame(width, height, layout);
    var body = packet[HeaderLength..];
    if (coded)
      frame._DecodeCoded(body);
    else
      frame._DecodeRaw(body);
    return frame.ToImage(flipped);
  }

  internal long RawSize => this.Layout switch {
    DxtoryLayout.Rgb565 or DxtoryLayout.Rgb555 => this.Width * (long)this.Height * 2,
    DxtoryLayout.Bgr24 or DxtoryLayout.Yuv444 => this.Width * (long)this.Height * 3,
    DxtoryLayout.Yuv420 => this.Width * (long)this.Height + 2L * ((this.Width + 1) >> 1) * ((this.Height + 1) >> 1),
    _ => this.Width * (long)this.Height + 2L * ((this.Width + 3) >> 2) * ((this.Height + 3) >> 2),
  };

  private void _DecodeRaw(ReadOnlySpan<byte> src) {
    if (src.Length < this.RawSize)
      throw new InvalidDataException($"This Dxtory frame is {src.Length} bytes; its raw {this.Layout} picture needs {this.RawSize}.");
    switch (this.Layout) {
      case DxtoryLayout.Yuv420:
        this._Raw420(src);
        break;
      case DxtoryLayout.Yuv410:
        this._Raw410(src);
        break;
      case DxtoryLayout.Yuv444: {
        var at = 0;
        for (var y = 0; y < this.Height; ++y)
          for (var x = 0; x < this.Width; ++x) {
            this.Planes[0][x, y] = src[at++];
            this.Planes[1][x, y] = (byte)(src[at++] ^ 0x80);
            this.Planes[2][x, y] = (byte)(src[at++] ^ 0x80);
          }
        break;
      }
      default: {
        var row = this.Planes[0].Width;
        for (var y = 0; y < this.Height; ++y)
          src.Slice(y * row, row).CopyTo(this.Planes[0].Data.AsSpan(y * this.Planes[0].Stride));
        break;
      }
    }
  }

  /// <summary>
  /// FFmpeg's <c>dxtory_decode_v1_420</c>. For an odd width FFmpeg writes each pair of rows' last luma
  /// sample one place too far right and never fills the last column; this writes it where it belongs.
  /// </summary>
  private void _Raw420(ReadOnlySpan<byte> src) {
    DxtoryPlane yp = this.Planes[0], up = this.Planes[1], vp = this.Planes[2];
    int height = this.Height & ~1, width = this.Width & ~1;
    int hmargin = this.Width - width, vmargin = this.Height - height;
    var border = ((this.Width + 1) >> 1) - 1;
    var at = 0;
    var y = 0;
    for (; y < height; y += 2) {
      for (var x = 0; x < width; x += 2) {
        yp[x, y] = src[at];
        yp[x + 1, y] = src[at + 1];
        yp[x, y + 1] = src[at + 2];
        yp[x + 1, y + 1] = src[at + 3];
        up[x >> 1, y >> 1] = (byte)(src[at + 4] + 0x80);
        vp[x >> 1, y >> 1] = (byte)(src[at + 5] + 0x80);
        at += 6;
      }
      if (hmargin != 0) {
        yp[width, y] = src[at];
        yp[width, y + 1] = src[at + 1];
        up[border, y >> 1] = (byte)(src[at + 2] + 0x80);
        vp[border, y >> 1] = (byte)(src[at + 3] + 0x80);
        at += 4;
      }
    }
    if (vmargin != 0) {
      var x = 0;
      for (; x < width; x += 2) {
        yp[x, y] = src[at];
        yp[x + 1, y] = src[at + 1];
        up[x >> 1, y >> 1] = (byte)(src[at] + 0x80);
        vp[x >> 1, y >> 1] = (byte)(src[at + 1] + 0x80);
        at += 4;
      }
      if (hmargin != 0) {
        yp[x, y] = src[at];
        up[border, y >> 1] = (byte)(src[at + 1] + 0x80);
        vp[border, y >> 1] = (byte)(src[at + 2] + 0x80);
      }
    }
  }

  /// <summary>FFmpeg's <c>dxtory_decode_v1_410</c>, its bottom-right corner included as FFmpeg writes it.</summary>
  private void _Raw410(ReadOnlySpan<byte> src) {
    DxtoryPlane yp = this.Planes[0], up = this.Planes[1], vp = this.Planes[2];
    int height = this.Height & ~3, width = this.Width & ~3;
    int hmargin = this.Width - width, vmargin = this.Height - height;
    var border = ((this.Width + 3) >> 2) - 1;
    var at = 0;
    var y = 0;
    for (; y < height; y += 4) {
      for (var x = 0; x < width; x += 4) {
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i)
            yp[x + i, y + j] = src[at + j * 4 + i];
        up[x >> 2, y >> 2] = (byte)(src[at + 16] + 0x80);
        vp[x >> 2, y >> 2] = (byte)(src[at + 17] + 0x80);
        at += 18;
      }
      if (hmargin != 0) {
        for (var w = 0; w < hmargin; ++w)
          for (var j = 0; j < 4; ++j)
            yp[width + w, y + j] = src[at + w + hmargin * j];
        at += 4 * hmargin;
        up[border, y >> 2] = (byte)(src[at] + 0x80);
        vp[border, y >> 2] = (byte)(src[at + 1] + 0x80);
        at += 2;
      }
    }
    if (vmargin != 0) {
      for (var x = 0; x < width; x += 4) {
        for (var j = 0; j < Math.Min(vmargin, 3); ++j)
          for (var i = 0; i < 4; ++i)
            yp[x + i, y + j] = src[at + j * 4 + i];
        at += 4 * vmargin;
        up[x >> 2, y >> 2] = (byte)(src[at] + 0x80);
        vp[x >> 2, y >> 2] = (byte)(src[at + 1] + 0x80);
        at += 2;
      }
      if (hmargin != 0) {
        // FFmpeg copies these to the start of the rows rather than past the last full column.
        for (var w = 0; w < hmargin; ++w) {
          for (var j = 0; j < Math.Min(vmargin, 3); ++j)
            for (var i = 0; i < 4; ++i)
              yp[w + i, y + j] = src[at + j * 4 + i];
          at += 4 * vmargin;
        }
        up[border, y >> 2] = (byte)(src[at] + 0x80);
        vp[border, y >> 2] = (byte)(src[at + 1] + 0x80);
      }
    }
  }

  private void _DecodeCoded(ReadOnlySpan<byte> src) {
    if (src.Length < 2)
      throw new InvalidDataException("This Dxtory frame ends before its slice count.");
    var slices = BinaryPrimitives.ReadUInt16LittleEndian(src);
    var offset = (slices * 4 + 2 + 15) & ~15;
    if (src.Length < offset)
      throw new InvalidDataException("This Dxtory frame ends inside its slice table.");
    if (slices == 0)
      throw new NotSupportedException("This Dxtory frame states no slices, which FFmpeg asks for a sample of.");

    long check = offset;
    for (var s = 0; s < slices; ++s) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(src[(2 + s * 4)..]);
      if (size <= 16 + (uint)(this.Height * this.Width / (8 * slices)))
        throw new InvalidDataException($"Dxtory slice {s} states {size} bytes, too few for its share of the picture.");
      check += size;
    }
    // FFmpeg tolerates a frame up to 95% short of its slices, as its default discard_damaged_percentage allows.
    if (check - 95 * check / 100 > src.Length)
      throw new InvalidDataException("This Dxtory frame is far shorter than its slices state.");

    var line = 0;
    for (var s = 0; s < slices; ++s) {
      var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(src[(2 + s * 4)..]);
      if (size > src.Length - offset)
        throw new InvalidDataException($"Dxtory slice {s} states {size} bytes; {src.Length - offset} remain.");
      var symbols = new DxtorySymbols(src.Slice(offset + 16, size - 16));
      line += this.Slice(symbols, line, this.Height - line);
      offset += size;
    }
  }

  private void _ResetLists(DxtorySymbols symbols) {
    switch (this.Layout) {
      case DxtoryLayout.Rgb555:
        symbols.Reset(DxtorySymbols.DefaultList555, DxtorySymbols.DefaultList555, DxtorySymbols.DefaultList555);
        break;
      case DxtoryLayout.Rgb565:
        symbols.Reset(DxtorySymbols.DefaultList555, DxtorySymbols.DefaultList565, DxtorySymbols.DefaultList555);
        break;
      default:
        symbols.Reset(DxtorySymbols.DefaultList, DxtorySymbols.DefaultList, DxtorySymbols.DefaultList);
        break;
    }
  }

  /// <summary>
  /// One slice of version 2 symbols from <paramref name="line"/>, at most <paramref name="left"/> lines, in
  /// FFmpeg's order — its odd-size paths included, which read a symbol or two for samples past the edge.
  /// Returns the lines FFmpeg counts as done. Reads or writes as <paramref name="symbols"/> does.
  /// </summary>
  internal int Slice(DxtorySymbols symbols, int line, int left) {
    this._ResetLists(symbols);
    return this.Layout switch {
      DxtoryLayout.Yuv420 => this._Slice420(symbols, line, left),
      DxtoryLayout.Yuv410 => this._Slice410(symbols, line, left),
      DxtoryLayout.Yuv444 => this._Slice444(symbols, line, left),
      DxtoryLayout.Bgr24 => this._SliceRgb(symbols, line, left),
      _ => this._Slice5x5(symbols, line, left, this.Layout == DxtoryLayout.Rgb565),
    };
  }

  private byte _Luma(DxtorySymbols s, int x, int y) => this.Planes[0][x, y] = s.Symbol(0, this.Planes[0][x, y]);

  private byte _Chroma(DxtorySymbols s, int plane, int x, int y)
    => this.Planes[plane][x, y] = (byte)(s.Symbol(plane, (byte)(this.Planes[plane][x, y] ^ 0x80)) ^ 0x80);

  private int _SliceRgb(DxtorySymbols s, int line, int left) {
    var plane = this.Planes[0];
    var y = 0;
    for (; y < left && s.BitsLeft >= 3 * this.Width; ++y)
      for (var x = 0; x < this.Width; ++x)
        for (var c = 0; c < 3; ++c)
          plane[x * 3 + c, line + y] = s.Symbol(c, plane[x * 3 + c, line + y]);
    return y;
  }

  /// <summary>
  /// FFmpeg's <c>dx2_decode_slice_5x5</c>: blue, green and red symbols of five or six bits, each widened to
  /// eight and stored red, green, blue. Read only: the encoder writes eight-bit layouts.
  /// </summary>
  private int _Slice5x5(DxtorySymbols s, int line, int left, bool is565) {
    if (s.Writing)
      throw new NotSupportedException("This encoder writes Dxtory's eight-bit layouts, not its 5- and 6-bit ones.");
    this._rgb ??= new(this.Width * 3, this.Height);
    var y = 0;
    for (; y < left && s.BitsLeft >= 3 * this.Width; ++y)
      for (var x = 0; x < this.Width; ++x) {
        var b = s.Symbol(0, 0, 5);
        var g = s.Symbol(1, 0, is565 ? 6 : 5);
        var r = s.Symbol(2, 0, 5);
        this._rgb[x * 3, line + y] = (byte)((r << 3) | (r >> 2));
        this._rgb[x * 3 + 1, line + y] = (byte)(is565 ? (g << 2) | (g >> 4) : (g << 3) | (g >> 2));
        this._rgb[x * 3 + 2, line + y] = (byte)((b << 3) | (b >> 2));
      }
    return y;
  }

  private DxtoryPlane? _rgb;

  private int _Slice420(DxtorySymbols s, int line, int left) {
    int w = this.Width & ~1, hmargin = this.Width - w, vmargin = this.Height - (this.Height & ~1);
    var border = ((this.Width + 1) >> 1) - 1;
    var cy = line >> 1;
    var y = 0;
    for (; y < left - 1 && s.BitsLeft >= 3 * w + hmargin * 4; y += 2, ++cy) {
      var x = 0;
      for (; x < w; x += 2) {
        this._Luma(s, x, line + y);
        this._Luma(s, x + 1, line + y);
        this._Luma(s, x, line + y + 1);
        this._Luma(s, x + 1, line + y + 1);
        this._Chroma(s, 1, x >> 1, cy);
        this._Chroma(s, 2, x >> 1, cy);
      }
      if (hmargin != 0) {
        this._Luma(s, x, line + y);
        this._Luma(s, x, line + y + 1);
        this._Chroma(s, 1, border, cy);
        this._Chroma(s, 2, border, cy);
      }
    }
    // FFmpeg reads this row whenever the picture's height is odd, at the end of every slice, and only
    // every other luma sample of it: the rest are never coded, and stay as they were.
    if (vmargin != 0) {
      var x = 0;
      for (; x < this.Width; x += 2) {
        this._Luma(s, x, line + y);
        this._Chroma(s, 1, x >> 1, cy);
        this._Chroma(s, 2, x >> 1, cy);
      }
      if (hmargin != 0) {
        this._Luma(s, x, line + y);
        this._Chroma(s, 1, border, cy);
        this._Chroma(s, 2, border, cy);
      }
    }
    return y;
  }

  private int _Slice410(DxtorySymbols s, int line, int left) {
    int w = this.Width & ~3, hmargin = this.Width - w, vmargin = this.Height - (this.Height & ~3);
    var border = ((this.Width + 3) >> 2) - 1;
    var cy = line >> 2;
    var y = 0;
    for (; y < left - 3 && s.BitsLeft >= 18 * w / 4 + hmargin * 4 + (hmargin != 0 ? 2 : 0); y += 4, ++cy) {
      var x = 0;
      for (; x < w; x += 4) {
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i)
            this._Luma(s, x + i, line + y + j);
        this._Chroma(s, 1, x >> 2, cy);
        this._Chroma(s, 2, x >> 2, cy);
      }
      if (hmargin != 0) {
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < hmargin; ++i)
            this._Luma(s, x + i, line + y + j);
        this._Chroma(s, 1, border, cy);
        this._Chroma(s, 2, border, cy);
      }
    }
    if (vmargin != 0 && y + vmargin == left) {
      var x = 0;
      for (; x < this.Width; x += 4) {
        for (var j = 0; j < vmargin; ++j)
          for (var i = 0; i < 4; ++i)
            this._Luma(s, x + i, line + y + j);
        this._Chroma(s, 1, x >> 2, cy);
        this._Chroma(s, 2, x >> 2, cy);
      }
      if (hmargin != 0) {
        for (var j = 0; j < vmargin; ++j)
          for (var i = 0; i < hmargin; ++i)
            this._Luma(s, x + i, line + y + j);
        this._Chroma(s, 1, border, cy);
        this._Chroma(s, 2, border, cy);
      }
      y += vmargin;
    }
    return y;
  }

  private int _Slice444(DxtorySymbols s, int line, int left) {
    var y = 0;
    for (; y < left && s.BitsLeft >= 3 * this.Width; ++y)
      for (var x = 0; x < this.Width; ++x) {
        this._Luma(s, x, line + y);
        this._Chroma(s, 1, x, line + y);
        this._Chroma(s, 2, x, line + y);
      }
    return y;
  }

  /// <summary>The picture as this package holds it.</summary>
  internal RawImage ToImage(bool flipped) {
    int width = this.Width, height = this.Height;
    switch (this.Layout) {
      case DxtoryLayout.Yuv420:
      case DxtoryLayout.Yuv410:
      case DxtoryLayout.Yuv444: {
        var format = this.Layout switch { DxtoryLayout.Yuv420 => PixelFormat.Yuv420P8, DxtoryLayout.Yuv410 => PixelFormat.Yuv410P8, _ => PixelFormat.Yuv444P8 };
        var size = 0;
        foreach (var plane in this.Planes)
          size += plane.Width * plane.Height;
        var pixels = new byte[size];
        var at = 0;
        foreach (var plane in this.Planes) {
          plane.CopyTo(pixels.AsSpan(at), flipped);
          at += plane.Width * plane.Height;
        }
        return new() { Width = width, Height = height, Format = format, PixelData = pixels, ColorInfo = RawImageColorInfo.Bt601Limited };
      }
      case DxtoryLayout.Bgr24: {
        var pixels = new byte[width * height * 3];
        this.Planes[0].CopyTo(pixels, flipped);
        return new() { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = pixels };
      }
      default: {
        if (this._rgb != null) {
          var widened = new byte[width * height * 3];
          this._rgb.CopyTo(widened, flipped);
          return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = widened };
        }
        var packed = new byte[width * height * 2];
        this.Planes[0].CopyTo(packed, flipped);
        if (this.Layout == DxtoryLayout.Rgb565)
          return new() { Width = width, Height = height, Format = PixelFormat.Rgb565, PixelData = packed };
        // RGB555 has no format of its own here; it is widened as FFmpeg widens version 2's.
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < width * height; ++i) {
          var v = packed[i * 2] | packed[i * 2 + 1] << 8;
          int r = (v >> 10) & 0x1F, g = (v >> 5) & 0x1F, b = v & 0x1F;
          rgb[i * 3] = (byte)((r << 3) | (r >> 2));
          rgb[i * 3 + 1] = (byte)((g << 3) | (g >> 2));
          rgb[i * 3 + 2] = (byte)((b << 3) | (b >> 2));
        }
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
    }
  }
}

/// <summary>Writes version 2 Dxtory frames: one slice of move-to-front symbols over the whole picture.</summary>
internal static class DxtoryEncoder {

  internal static uint TypeOf(DxtoryLayout layout) => layout switch {
    DxtoryLayout.Yuv420 => 0x02000009,
    DxtoryLayout.Yuv410 => 0x03000009,
    DxtoryLayout.Yuv444 => 0x04000009,
    _ => 0x01000009,
  };

  /// <summary>One frame from its planes, top row first: luma and two chroma planes, or BGR samples.</summary>
  internal static byte[] Encode(int width, int height, DxtoryLayout layout, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third) {
    var frame = new DxtoryFrame(width, height, layout);
    frame.Planes[0].CopyFrom(first);
    if (frame.Planes.Length == 3) {
      frame.Planes[1].CopyFrom(second);
      frame.Planes[2].CopyFrom(third);
    }
    var symbols = new DxtorySymbols();
    frame.Slice(symbols, 0, height);
    var data = new List<byte>(symbols.ToArray());
    // FFmpeg wants a slice of more than an eighth of a byte a pixel; the symbols always give more, but
    // a picture could in principle come in under it, so the slice is padded.
    while (data.Count <= width * height / 8)
      data.Add(0);

    var packet = new byte[DxtoryFrame.HeaderLength + 16 + 16 + data.Count];
    BinaryPrimitives.WriteUInt32BigEndian(packet, TypeOf(layout));
    var body = packet.AsSpan(DxtoryFrame.HeaderLength);
    BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(body[2..], (uint)(16 + data.Count));
    // The slice table is padded to sixteen bytes; each slice opens with sixteen bytes of its own, the
    // first four its length after them.
    BinaryPrimitives.WriteUInt32LittleEndian(body[16..], (uint)data.Count);
    data.CopyTo(packet, DxtoryFrame.HeaderLength + 32);
    return packet;
  }
}
