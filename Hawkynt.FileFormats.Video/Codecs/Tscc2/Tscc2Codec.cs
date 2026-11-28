using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Tscc2;

/// <summary>A canonical prefix code read and written least-significant bit first, as FFmpeg builds one from lengths.</summary>
internal sealed class Tscc2Vlc {

  private const int _PEEK = 12;

  private readonly int[] _symbol = new int[1 << _PEEK];
  private readonly byte[] _length = new byte[1 << _PEEK];
  private readonly Dictionary<int, (int Pattern, int Length)> _codes = [];

  /// <summary>Codes assigned in listed order, each the next free code of its length, as <c>ff_vlc_init_from_lengths</c> assigns them.</summary>
  internal Tscc2Vlc(ReadOnlySpan<byte> lengths, ReadOnlySpan<int> symbols) {
    ulong code = 0;
    for (var i = 0; i < lengths.Length; ++i) {
      var length = lengths[i];
      if (length == 0)
        continue;
      var canonical = (int)(code >> (32 - length));
      // Read least-significant bit first, the code's first bit is the first one read.
      var pattern = 0;
      for (var j = 0; j < length; ++j)
        pattern |= ((canonical >> (length - 1 - j)) & 1) << j;
      for (var fill = pattern; fill < 1 << _PEEK; fill += 1 << length) {
        this._symbol[fill] = symbols[i];
        this._length[fill] = length;
      }
      this._codes[symbols[i]] = (pattern, length);
      code += 1UL << (32 - length);
    }
  }

  internal int Read(ref Tscc2BitReader reader) {
    var next = reader.Peek(_PEEK);
    var length = this._length[next];
    if (length == 0)
      throw new InvalidDataException("A TSCC2 code matches no entry of its table.");
    reader.Skip(length);
    return this._symbol[next];
  }

  internal bool TryWrite(Tscc2BitWriter writer, int symbol) {
    if (!this._codes.TryGetValue(symbol, out var code))
      return false;
    writer.Write(code.Pattern, code.Length);
    return true;
  }
}

/// <summary>Reads bits least-significant first; past the end every bit is zero.</summary>
internal ref struct Tscc2BitReader(ReadOnlySpan<byte> data) {

  private readonly ReadOnlySpan<byte> _data = data;
  private int _bit;

  internal readonly int Left => this._data.Length * 8 - this._bit;

  internal readonly int Peek(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var at = this._bit + i;
      if (at >> 3 < this._data.Length)
        value |= ((this._data[at >> 3] >> (at & 7)) & 1) << i;
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

/// <summary>Writes bits least-significant first.</summary>
internal sealed class Tscc2BitWriter {
  private readonly List<byte> _bytes = [];
  private int _current;
  private int _filled;

  internal int BitCount => this._bytes.Count * 8 + this._filled;

  internal void Write(int value, int count) {
    for (var i = 0; i < count; ++i) {
      this._current |= ((value >> i) & 1) << this._filled;
      if (++this._filled == 8) {
        this._bytes.Add((byte)this._current);
        this._current = 0;
        this._filled = 0;
      }
    }
  }

  internal void Append(Tscc2BitWriter other) {
    foreach (var b in other._bytes)
      this.Write(b, 8);
    this.Write(other._current, other._filled);
  }

  internal byte[] ToArray() {
    var result = new List<byte>(this._bytes);
    if (this._filled != 0)
      result.Add((byte)this._current);
    return [.. result];
  }
}

/// <summary>The tables built once, and the transform TSCC2's blocks share.</summary>
internal static class Tscc2Transform {

  internal static readonly Tscc2Vlc Dc = new(Tscc2Tables.DcLengths, Tscc2Tables.DcSymbols);
  internal static readonly Tscc2Vlc[] Count = _Sets(static set => new(
    Tscc2Tables.CountLengths.AsSpan(set * 16, 16), Array.ConvertAll(Tscc2Tables.CountSymbols[(set * 16)..(set * 16 + 16)], static b => (int)b)));
  internal static readonly Tscc2Vlc[] Ac = _BuildAc();

  private static Tscc2Vlc[] _Sets(Func<int, Tscc2Vlc> build) {
    var sets = new Tscc2Vlc[Tscc2Tables.VlcSets];
    for (var i = 0; i < sets.Length; ++i)
      sets[i] = build(i);
    return sets;
  }

  private static Tscc2Vlc[] _BuildAc() {
    var sets = new Tscc2Vlc[Tscc2Tables.VlcSets];
    var at = 0;
    for (var i = 0; i < sets.Length; ++i) {
      var size = Tscc2Tables.AcSizes[i];
      sets[i] = new(Tscc2Tables.AcLengths.AsSpan(at, size), Tscc2Tables.AcSymbols.AsSpan(at, size));
      at += size;
    }
    return sets;
  }

  /// <summary>The three dequantisers of quantiser <paramref name="quant"/>, 2 to 14.</summary>
  internal static ReadOnlySpan<int> Dequantisers(int quant) => Tscc2Tables.Quants.AsSpan((quant - 2) * 3, 3);

  private static int _Dequant(int q, int value) => (q * value + 0x80) >> 8;

  /// <summary>FFmpeg's <c>tscc2_idct4_put</c>: dequantise, transform, and store each sample's low eight bits.</summary>
  internal static void InverseTransform(ReadOnlySpan<int> block, ReadOnlySpan<int> q, Span<byte> destination, int at, int stride) {
    Span<int> t = stackalloc int[16];
    for (var i = 0; i < 4; ++i) {
      var t0 = _Dequant(q[i & 1], block[i]);
      var t1 = _Dequant(q[1 + (i & 1)], block[4 + i]);
      var t2 = _Dequant(q[i & 1], block[8 + i]);
      var t3 = _Dequant(q[1 + (i & 1)], block[12 + i]);
      t[i] = 5 * (t0 + t1 + t2) + 2 * t3;
      t[4 + i] = 5 * (t0 - t2 - t3) + 2 * t1;
      t[8 + i] = 5 * (t0 - t2 + t3) - 2 * t1;
      t[12 + i] = 5 * (t0 - t1 + t2) - 2 * t3;
    }
    for (var i = 0; i < 4; ++i) {
      int s0 = t[i * 4], s1 = t[i * 4 + 1], s2 = t[i * 4 + 2], s3 = t[i * 4 + 3];
      var row = at + i * stride;
      destination[row] = unchecked((byte)((5 * (s0 + s1 + s2) + 2 * s3 + 0x20) >> 6));
      destination[row + 1] = unchecked((byte)((5 * (s0 - s2 - s3) + 2 * s1 + 0x20) >> 6));
      destination[row + 2] = unchecked((byte)((5 * (s0 - s2 + s3) - 2 * s1 + 0x20) >> 6));
      destination[row + 3] = unchecked((byte)((5 * (s0 - s1 + s2) - 2 * s3 + 0x20) >> 6));
    }
  }

  /// <summary>The inverse of the one-dimensional transform's matrix, for finding coefficients that reproduce a block.</summary>
  private static readonly double[,] _Inverse = _Invert(new double[,] { { 5, 5, 5, 2 }, { 5, 2, -5, -5 }, { 5, -2, -5, 5 }, { 5, -5, 5, -2 } });

  private static double[,] _Invert(double[,] m) {
    var a = new double[4, 8];
    for (var r = 0; r < 4; ++r) {
      for (var c = 0; c < 4; ++c)
        a[r, c] = m[r, c];
      a[r, 4 + r] = 1;
    }
    for (var col = 0; col < 4; ++col) {
      var pivot = col;
      for (var r = col + 1; r < 4; ++r)
        if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
          pivot = r;
      for (var c = 0; c < 8; ++c)
        (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
      var div = a[col, col];
      for (var c = 0; c < 8; ++c)
        a[col, c] /= div;
      for (var r = 0; r < 4; ++r)
        if (r != col) {
          var f = a[r, col];
          for (var c = 0; c < 8; ++c)
            a[r, c] -= f * a[col, c];
        }
    }
    var inverse = new double[4, 4];
    for (var r = 0; r < 4; ++r)
      for (var c = 0; c < 4; ++c)
        inverse[r, c] = a[r, 4 + c];
    return inverse;
  }

  /// <summary>
  /// Coefficients whose inverse transform is exactly <paramref name="pixels"/>, or false: the dequantised
  /// block is 64 times the inverse matrix about the pixels, and each is rounded to its quantiser and checked.
  /// </summary>
  internal static bool TryForward(ReadOnlySpan<byte> pixels, ReadOnlySpan<int> q, Span<int> coefficients) {
    Span<double> half = stackalloc double[16];
    for (var r = 0; r < 4; ++r)
      for (var x = 0; x < 4; ++x) {
        double sum = 0;
        for (var y = 0; y < 4; ++y)
          sum += _Inverse[r, y] * pixels[y * 4 + x];
        half[r * 4 + x] = sum;
      }
    for (var r = 0; r < 4; ++r)
      for (var c = 0; c < 4; ++c) {
        double sum = 0;
        for (var x = 0; x < 4; ++x)
          sum += half[r * 4 + x] * _Inverse[c, x];
        var dequantised = 64 * sum;
        var level = (int)Math.Round(dequantised * 256 / q[(r & 1) + (c & 1)]);
        coefficients[r * 4 + c] = r == 0 && c == 0 ? Math.Clamp(level, 0, 255) : Math.Clamp(level, -128, 127);
      }
    Span<byte> back = stackalloc byte[16];
    InverseTransform(coefficients, q, back, 0, 4);
    return back.SequenceEqual(pixels);
  }
}

/// <summary>
/// The TSCC2 picture decoder, FFmpeg's <c>libavcodec/tscc2.c</c> (LGPL-2.1-or-later): three full-size
/// planes in sixteen-by-eight macroblocks, each plane of one a fill, raw samples, or eight four-by-four
/// transformed blocks.
/// </summary>
internal sealed class Tscc2FrameDecoder {

  internal readonly int MacroblocksWide;
  internal readonly int MacroblocksHigh;
  internal readonly int Stride;
  internal readonly byte[][] Planes;
  private readonly byte[] _quants;
  private readonly int[] _block = new int[16];

  internal Tscc2FrameDecoder(int width, int height) {
    this.MacroblocksWide = (width + 15) >> 4;
    this.MacroblocksHigh = (height + 7) >> 3;
    this.Stride = this.MacroblocksWide * 16;
    this.Planes = [new byte[this.Stride * this.MacroblocksHigh * 8], new byte[this.Stride * this.MacroblocksHigh * 8], new byte[this.Stride * this.MacroblocksHigh * 8]];
    this._quants = new byte[this.MacroblocksWide * this.MacroblocksHigh];
  }

  /// <summary>Applies one packet; false for a frame that repeats the last, which yields no picture.</summary>
  internal bool Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 1)
      throw new InvalidDataException("A TSCC2 frame is at least its type byte.");
    var type = packet[0];
    if (type > 1)
      throw new InvalidDataException($"TSCC2 frame type {type} is not defined; 0 repeats the last frame and 1 codes one.");
    if (type == 0)
      return false;
    if (packet.Length - 1 < 4)
      throw new InvalidDataException("This TSCC2 frame is too short for its header.");

    int quant0 = packet[1], quant1 = packet[2];
    if (quant0 < 2 || quant0 > Tscc2Tables.VlcSets + 1 || quant1 < 2 || quant1 > Tscc2Tables.VlcSets + 1)
      throw new InvalidDataException($"TSCC2 quantisers run 2 to {Tscc2Tables.VlcSets + 1}; this frame states {quant0} and {quant1}.");
    var at = 4;
    if (packet.Length - at < 4)
      throw new InvalidDataException("This TSCC2 frame ends before its slice properties.");
    var size = BinaryPrimitives.ReadUInt32LittleEndian(packet[at..]);
    at += 4;
    if (size > (uint)(packet.Length - at))
      throw new InvalidDataException("This TSCC2 frame's slice properties run past its end.");

    var count = this._quants.Length;
    var position = 0;
    for (var i = 0; i < size; ++i) {
      var value = packet[at++];
      var length = value & 0x3F;
      if (position + length > count)
        throw new InvalidDataException("This TSCC2 frame states more slice properties than it has macroblocks.");
      this._quants.AsSpan(position, length).Fill((byte)(value >> 6));
      position += length;
    }
    if (position < count)
      throw new InvalidDataException($"This TSCC2 frame states {position} slice properties for {count} macroblocks.");

    for (var row = 0; row < this.MacroblocksHigh; ++row) {
      if (at >= packet.Length)
        throw new InvalidDataException($"This TSCC2 frame ends before row {row}.");
      int length;
      if ((packet[at] & 1) != 0)
        length = packet[at++] - 1;
      else {
        if (packet.Length - at < 4)
          throw new InvalidDataException($"This TSCC2 frame ends inside row {row}'s size.");
        length = (int)(BinaryPrimitives.ReadUInt32LittleEndian(packet[at..]) >> 1);
        at += 4;
      }
      if (length == 0)
        for (var x = 0; x < this.MacroblocksWide; ++x)
          if (this._quants[row * this.MacroblocksWide + x] is 1 or 2)
            throw new InvalidDataException($"TSCC2 row {row} is empty but codes macroblocks.");
      if (packet.Length - at < length)
        throw new InvalidDataException($"TSCC2 row {row} states {length} bytes; {packet.Length - at} remain.");
      this._DecodeRow(row, packet.Slice(at, length), quant0, quant1);
      at += length;
    }
    return true;
  }

  private void _DecodeRow(int row, ReadOnlySpan<byte> data, int quant0, int quant1) {
    var reader = new Tscc2BitReader(data);
    for (var x = 0; x < this.MacroblocksWide; ++x) {
      var q = this._quants[row * this.MacroblocksWide + x];
      if (q is 0 or 3)
        continue; // skipped: the last picture stands
      var quant = q == 1 ? quant0 : quant1;
      for (var plane = 0; plane < 3; ++plane)
        this._DecodeMacroblock(ref reader, Tscc2Transform.Dequantisers(quant), quant - 2, this.Planes[plane], row * 8 * this.Stride + x * 16);
    }
  }

  private void _DecodeMacroblock(ref Tscc2BitReader reader, ReadOnlySpan<int> q, int set, byte[] plane, int at) {
    var stride = this.Stride;
    if (reader.Read(1) != 0) {
      if (reader.Read(1) != 0) {
        var value = (byte)reader.Read(8);
        for (var y = 0; y < 8; ++y)
          plane.AsSpan(at + y * stride, 16).Fill(value);
      } else {
        if (reader.Left < 16 * 8 * 8)
          throw new InvalidDataException("A raw TSCC2 macroblock runs past its row.");
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 16; ++x)
            plane[at + y * stride + x] = (byte)reader.Read(8);
      }
      return;
    }

    var previous = 0;
    var block = this._block.AsSpan();
    for (var j = 0; j < 2; ++j) {
      for (var k = 0; k < 4; ++k) {
        int dc;
        if (j == 0 && k == 0)
          dc = reader.Read(8);
        else {
          dc = Tscc2Transform.Dc.Read(ref reader);
          if (dc == Tscc2Tables.DcEscape)
            dc = reader.Read(8);
        }
        dc = (dc + previous) & 0xFF;
        previous = dc;
        block.Clear();
        block[0] = dc;

        var count = Tscc2Transform.Count[set].Read(ref reader);
        var position = 1;
        for (var l = 0; l < count; ++l) {
          var ac = Tscc2Transform.Ac[set].Read(ref reader);
          if (ac == Tscc2Tables.AcEscape)
            ac = reader.Read(12);
          position += ac & 0xF;
          if (position >= 16)
            throw new InvalidDataException("A TSCC2 block's coefficients run past its sixteen.");
          block[Tscc2Tables.ZigZag[position++]] = (sbyte)(ac >> 4);
        }
        Tscc2Transform.InverseTransform(block, q, plane, at + k * 4, stride);
      }
      at += 4 * stride;
    }
  }
}

/// <summary>
/// Writes TSCC2 frames losslessly: an unchanged macroblock skipped, and each plane of the rest the cheapest
/// of a fill, the transform where its coefficients reproduce the samples exactly, and raw samples.
/// </summary>
internal sealed class Tscc2Encoder {

  /// <summary>Quantiser 2, the finest, whose transform reproduces the most blocks exactly.</summary>
  private const int _QUANT = 2;

  private readonly int _width;
  private readonly int _height;
  private readonly Tscc2FrameDecoder _reference;
  private bool _started;

  internal Tscc2Encoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._reference = new(width, height);
  }

  /// <summary>One frame from three full-size planes; the decoder's reading of it becomes the next reference.</summary>
  internal byte[] Encode(ReadOnlySpan<byte> y, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v) {
    var reference = this._reference;
    int wide = reference.MacroblocksWide, high = reference.MacroblocksHigh, stride = reference.Stride;
    var source = new byte[3][];
    for (var p = 0; p < 3; ++p)
      source[p] = _Pad(p switch { 0 => y, 1 => u, _ => v }, this._width, this._height, stride, high * 8);

    var quants = new byte[wide * high];
    var rows = new byte[high][];
    var q = Tscc2Transform.Dequantisers(_QUANT);
    for (var row = 0; row < high; ++row) {
      var writer = new Tscc2BitWriter();
      for (var x = 0; x < wide; ++x) {
        var at = row * 8 * stride + x * 16;
        if (this._started && _Same(source, reference.Planes, at, stride))
          continue; // property 0: skipped
        quants[row * wide + x] = 1;
        for (var p = 0; p < 3; ++p)
          _EncodeMacroblock(writer, source[p], at, stride, q, _QUANT - 2);
      }
      rows[row] = writer.ToArray();
    }

    using var output = new MemoryStream();
    output.WriteByte(1);
    output.WriteByte(_QUANT);
    output.WriteByte(_QUANT);
    output.WriteByte(0);
    var properties = new List<byte>();
    for (var i = 0; i < quants.Length;) {
      var run = 1;
      while (i + run < quants.Length && quants[i + run] == quants[i] && run < 0x3F)
        ++run;
      properties.Add((byte)(quants[i] << 6 | run));
      i += run;
    }
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)properties.Count);
    output.Write(word);
    output.Write(properties.ToArray());
    foreach (var data in rows) {
      // An even length below 255 fits one odd byte; any other goes as a doubled 32-bit word.
      var length = data.Length;
      if (length % 2 == 0 && length + 1 <= 0xFF)
        output.WriteByte((byte)(length + 1));
      else {
        BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)length << 1);
        output.Write(word);
      }
      output.Write(data);
    }

    var packet = output.ToArray();
    reference.Decode(packet);
    this._started = true;
    return packet;
  }

  private static byte[] _Pad(ReadOnlySpan<byte> plane, int width, int height, int stride, int rows) {
    var padded = new byte[stride * rows];
    for (var y = 0; y < rows; ++y) {
      var sourceRow = Math.Min(y, height - 1) * width;
      for (var x = 0; x < stride; ++x)
        padded[y * stride + x] = plane[sourceRow + Math.Min(x, width - 1)];
    }
    return padded;
  }

  private static bool _Same(byte[][] source, byte[][] reference, int at, int stride) {
    for (var p = 0; p < 3; ++p)
      for (var y = 0; y < 8; ++y)
        if (!source[p].AsSpan(at + y * stride, 16).SequenceEqual(reference[p].AsSpan(at + y * stride, 16)))
          return false;
    return true;
  }

  private static void _EncodeMacroblock(Tscc2BitWriter writer, byte[] plane, int at, int stride, ReadOnlySpan<int> q, int set) {
    var first = plane[at];
    var uniform = true;
    for (var y = 0; y < 8 && uniform; ++y)
      for (var x = 0; x < 16; ++x)
        if (plane[at + y * stride + x] != first) {
          uniform = false;
          break;
        }
    if (uniform) {
      writer.Write(1, 1);
      writer.Write(1, 1);
      writer.Write(first, 8);
      return;
    }

    var transformed = _TryTransform(plane, at, stride, q, set);
    if (transformed != null && transformed.BitCount < 2 + 16 * 8 * 8) {
      writer.Append(transformed);
      return;
    }
    writer.Write(1, 1);
    writer.Write(0, 1);
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 16; ++x)
        writer.Write(plane[at + y * stride + x], 8);
  }

  /// <summary>The macroblock as eight transformed blocks, or null where one of them cannot be reproduced exactly.</summary>
  private static Tscc2BitWriter? _TryTransform(byte[] plane, int at, int stride, ReadOnlySpan<int> q, int set) {
    var writer = new Tscc2BitWriter();
    writer.Write(0, 1);
    Span<byte> pixels = stackalloc byte[16];
    Span<int> coefficients = stackalloc int[16];
    var previous = 0;
    for (var j = 0; j < 2; ++j)
      for (var k = 0; k < 4; ++k) {
        var origin = at + j * 4 * stride + k * 4;
        for (var y = 0; y < 4; ++y)
          for (var x = 0; x < 4; ++x)
            pixels[y * 4 + x] = plane[origin + y * stride + x];
        if (!Tscc2Transform.TryForward(pixels, q, coefficients))
          return null;

        var dc = coefficients[0];
        if (j == 0 && k == 0)
          writer.Write(dc, 8);
        else {
          var difference = (dc - previous) & 0xFF;
          if (!Tscc2Transform.Dc.TryWrite(writer, difference)) {
            Tscc2Transform.Dc.TryWrite(writer, Tscc2Tables.DcEscape);
            writer.Write(difference, 8);
          }
        }
        previous = dc;

        var count = 0;
        for (var z = 1; z < 16; ++z)
          if (coefficients[Tscc2Tables.ZigZag[z]] != 0)
            ++count;
        Tscc2Transform.Count[set].TryWrite(writer, count);
        var position = 1;
        for (var z = 1; z < 16; ++z) {
          var level = coefficients[Tscc2Tables.ZigZag[z]];
          if (level == 0)
            continue;
          var symbol = (level & 0xFF) << 4 | (z - position);
          if (!Tscc2Transform.Ac[set].TryWrite(writer, symbol)) {
            Tscc2Transform.Ac[set].TryWrite(writer, Tscc2Tables.AcEscape);
            writer.Write(symbol, 12);
          }
          position = z + 1;
        }
      }
    return writer;
  }
}
