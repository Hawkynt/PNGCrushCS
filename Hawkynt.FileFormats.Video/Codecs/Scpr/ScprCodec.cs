using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Scpr;

/// <summary>An adaptive frequency table over 256 values with sixteen running sums, FFmpeg's <c>PixelModel</c>.</summary>
internal sealed class ScprPixelModel {
  internal readonly uint[] Freq = new uint[256];
  internal readonly uint[] Lookup = new uint[16];
  internal uint Total;

  internal ScprPixelModel() => this.Reset();

  internal void Reset() {
    Array.Fill(this.Freq, 1u);
    Array.Fill(this.Lookup, 16u);
    this.Total = 256;
  }
}

/// <summary>
/// The models ScreenPressor's first two generations share, FFmpeg's <c>SCPRContext</c>: three components'
/// 4096 contexts each, and the run, operation, range, count, fill, sub-rectangle and motion tables.
/// </summary>
internal sealed class ScprModels {

  internal const uint Bottom = 0x010000;

  private readonly ScprPixelModel?[] _pixel = new ScprPixelModel?[3 * 4096];
  internal readonly uint[][] Op = _Tables(6, 7);
  internal readonly uint[][] Run = _Tables(6, 257);
  internal readonly uint[] Range = new uint[257];
  internal readonly uint[] Count = new uint[257];
  internal readonly uint[] Fill = new uint[6];
  internal readonly uint[][] Sxy = _Tables(4, 17);
  internal readonly uint[][] Mv = _Tables(2, 513);

  private static uint[][] _Tables(int count, int size) {
    var tables = new uint[count][];
    for (var i = 0; i < count; ++i)
      tables[i] = new uint[size];
    return tables;
  }

  /// <summary>One component's model for a context; a model never used is FFmpeg's untouched one.</summary>
  internal ScprPixelModel Pixel(int component, int context) => this._pixel[component * 4096 + context] ??= new();

  /// <summary>FFmpeg's <c>reinit_tables</c>: every used pixel model whose total has moved off 256 starts again; every other table always does.</summary>
  internal void Reinitialise() {
    foreach (var model in this._pixel)
      if (model != null && model.Total != 256)
        model.Reset();
    static void Flat(uint[] table, int count) {
      Array.Fill(table, 1u, 0, count);
      table[count] = (uint)count;
    }
    foreach (var table in this.Run)
      Flat(table, 256);
    foreach (var table in this.Op)
      Flat(table, 6);
    Flat(this.Range, 256);
    Flat(this.Count, 256);
    Flat(this.Fill, 5);
    foreach (var table in this.Sxy)
      Flat(table, 16);
    foreach (var table in this.Mv)
      Flat(table, 512);
  }

  /// <summary>FFmpeg's update after a value of a small table: its count steps up, and all halve once the total passes 65536.</summary>
  internal static void Update(uint[] table, int count, int value, uint step) {
    var total = table[count];
    table[value] += step;
    total += step;
    if (total > Bottom) {
      total = 0;
      for (var i = 0; i < count; ++i) {
        var halved = (table[i] >> 1) + 1;
        table[i] = halved;
        total += halved;
      }
    }
    table[count] = total;
  }

  /// <summary>FFmpeg's update after a pixel value, its sums kept with it.</summary>
  internal static void Update(ScprPixelModel model, int value, uint step) {
    model.Freq[value] += step;
    model.Lookup[value >> 4] += step;
    var total = model.Total + step;
    if (total > Bottom) {
      total = 0;
      for (var i = 0; i < 256; ++i) {
        var halved = (model.Freq[i] >> 1) + 1;
        model.Freq[i] = halved;
        total += halved;
      }
      for (var i = 0; i < 16; ++i) {
        uint sum = 0;
        for (var j = 0; j < 16; ++j)
          sum += model.Freq[i * 16 + j];
        model.Lookup[i] = sum;
      }
    }
    model.Total = total;
  }
}

/// <summary>
/// ScreenPressor's two range decoders: version 2's, which scales the range by the total, and version 1's,
/// which divides exactly and keeps a second code; FFmpeg's <c>decode</c>/<c>get_freq</c> and
/// <c>decode0</c>/<c>get_freq0</c>.
/// </summary>
internal sealed class ScprRangeDecoder {

  private const uint _TOP = 0x01000000;

  private readonly byte[] _data;
  private int _at;
  private readonly bool _exact;
  private uint _code;
  private uint _range;
  private uint _code1;

  internal ScprRangeDecoder(byte[] data, int at, bool versionOne) {
    this._data = data;
    this._at = at;
    this._exact = versionOne;
    this._range = 0xFFFFFFFFu;
    this._code = 0;
    for (var i = 0; i < 4; ++i)
      this._code = this._code << 8 | this._Next();
  }

  internal int Left => this._data.Length - this._at;

  private uint _Next() => this._at < this._data.Length ? this._data[this._at++] : 0u;

  private uint _Frequency(uint total) {
    if (this._exact) {
      if (this._range == 0)
        throw new InvalidDataException("A ScreenPressor range has run to nothing.");
      return (uint)(total * (ulong)(this._code - this._code1) / this._range);
    }
    if (total == 0)
      throw new InvalidDataException("A ScreenPressor table has no total.");
    this._range /= total;
    if (this._range == 0)
      throw new InvalidDataException("A ScreenPressor range has run to nothing.");
    return this._code / this._range;
  }

  private void _Consume(uint cumulative, uint frequency, uint total) {
    if (this._exact) {
      var t = (uint)(this._range * (ulong)cumulative / total);
      this._code1 += t + 1;
      this._range = (uint)(this._range * (ulong)(frequency + cumulative) / total) - (t + 1);
      while (this._range < _TOP && this.Left > 0) {
        this._code = this._code << 8 | this._data[this._at++];
        this._code1 <<= 8;
        this._range <<= 8;
      }
      return;
    }
    this._code -= cumulative * this._range;
    this._range *= frequency;
    while (this._range < _TOP && this.Left > 0) {
      this._code = this._code << 8 | this._data[this._at++];
      this._range <<= 8;
    }
  }

  /// <summary>FFmpeg's <c>decode_value</c>.</summary>
  internal int Value(uint[] table, int count, uint step) {
    var total = table[count];
    var value = this._Frequency(total);
    uint cumulative = 0, frequency = 0;
    var c = 0;
    for (; c < count; ++c) {
      frequency = table[c];
      if (value >= cumulative + frequency)
        cumulative += frequency;
      else
        break;
    }
    if (c >= count)
      throw new InvalidDataException("A ScreenPressor value lies past its table.");
    this._Consume(cumulative, frequency, total);
    ScprModels.Update(table, count, c, step);
    return c;
  }

  /// <summary>FFmpeg's <c>decode_unit</c>: a group of sixteen found by its running sum, then the value within it.</summary>
  internal int Unit(ScprPixelModel model, uint step) {
    var total = model.Total;
    var value = this._Frequency(total);
    uint cumulative = 0, sum = 0;
    var x = 0;
    for (; x < 16; ++x) {
      sum = model.Lookup[x];
      if (value >= cumulative + sum)
        cumulative += sum;
      else
        break;
    }
    var c = x * 16;
    uint frequency = 0;
    for (; c < 256; ++c) {
      frequency = model.Freq[c];
      if (value >= cumulative + frequency)
        cumulative += frequency;
      else
        break;
    }
    if (x >= 16 || c >= 256)
      throw new InvalidDataException("A ScreenPressor sample lies past its table.");
    this._Consume(cumulative, frequency, total);
    ScprModels.Update(model, c, step);
    return c;
  }
}

/// <summary>A range encoder whose output version 2's decoder reads: carries propagate into what is already written.</summary>
internal sealed class ScprRangeEncoder {

  private const uint _TOP = 0x01000000;

  private readonly List<byte> _output;
  private readonly int _start;
  private ulong _low;
  private uint _range = 0xFFFFFFFFu;

  internal ScprRangeEncoder(List<byte> output) {
    this._output = output;
    this._start = output.Count;
  }

  private void _Encode(uint cumulative, uint frequency, uint total) {
    var r = this._range / total;
    this._low += (ulong)cumulative * r;
    this._range = r * frequency;
    if (this._low > 0xFFFFFFFFu) {
      for (var i = this._output.Count - 1; i >= this._start; --i)
        if (++this._output[i] != 0)
          break;
      this._low &= 0xFFFFFFFFu;
    }
    while (this._range < _TOP) {
      this._output.Add((byte)(this._low >> 24));
      this._low = (this._low << 8) & 0xFFFFFFFFu;
      this._range <<= 8;
    }
  }

  internal void Value(uint[] table, int count, int value, uint step) {
    uint cumulative = 0;
    for (var i = 0; i < value; ++i)
      cumulative += table[i];
    this._Encode(cumulative, table[value], table[count]);
    ScprModels.Update(table, count, value, step);
  }

  internal void Unit(ScprPixelModel model, int value, uint step) {
    uint cumulative = 0;
    for (var i = 0; i < value >> 4; ++i)
      cumulative += model.Lookup[i];
    for (var i = value & ~15; i < value; ++i)
      cumulative += model.Freq[i];
    this._Encode(cumulative, model.Freq[value], model.Total);
    ScprModels.Update(model, value, step);
  }

  internal void Finish() {
    for (var i = 0; i < 4; ++i) {
      this._output.Add((byte)(this._low >> 24));
      this._low = (this._low << 8) & 0xFFFFFFFFu;
    }
  }
}

/// <summary>
/// The ScreenPressor picture decoder for the first two generations and solid fills, FFmpeg's
/// <c>libavcodec/scpr.c</c> and <c>scpr.h</c> (LGPL-2.1-or-later). Pixels are held as FFmpeg holds them,
/// <c>(third &lt;&lt; 16) | (second &lt;&lt; 8) | first</c>, rows in coded order.
/// </summary>
internal sealed class ScprDecoder(int width, int height, int bitsPerPixel) {

  private readonly ScprModels _models = new();
  private uint[] _current = new uint[width * height];
  private uint[] _last = new uint[width * height];
  private readonly int _blocksWide = (width + 15) / 16;
  private readonly int _blocksHigh = (height + 15) / 16;
  private readonly int _shift = bitsPerPixel == 16 ? 0 : 2;
  private readonly int _bits = bitsPerPixel == 16 ? 0x1F : 0xFF;
  private int _version;

  /// <summary>The last picture shown, in coded row order.</summary>
  internal uint[] Picture => this._last;

  /// <summary>Applies a frame; false for an inter frame that changes nothing, which yields no picture.</summary>
  internal bool Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 1)
      throw new InvalidDataException("A ScreenPressor frame is at least its type byte.");
    var data = packet.ToArray();
    var type = data[0];
    ScprRangeDecoder? rc = null;
    switch (type) {
      case 2 or 18:
        this._version = type == 2 ? 1 : 2;
        rc = new(data, 2, this._version == 1);
        this._Intra(rc);
        break;
      case 34:
        throw new NotSupportedException("This ScreenPressor frame is of the third generation, which this decoder does not read.");
      case 17 or 33: {
        // The colour follows the type byte.
        if (data.Length < 3)
          throw new InvalidDataException("This ScreenPressor fill frame ends before its colour.");
        uint colour;
        if (bitsPerPixel == 16) {
          var value = data[1] | data[2] << 8;
          colour = (uint)(((value & 31) << 16) | (((value >> 5) & 31) << 8) | ((value >> 10) & 31));
        } else {
          if (data.Length < 4)
            throw new InvalidDataException("This ScreenPressor fill frame ends before its colour.");
          colour = (uint)(data[1] | data[2] << 8 | data[3] << 16);
        }
        Array.Fill(this._current, colour);
        break;
      }
      case 0 or 1:
        if (this._version == 0)
          throw new InvalidDataException("A ScreenPressor inter frame arrived before any key frame.");
        if (data.Length < 1 || data[0] == 0)
          return false; // nothing changed; FFmpeg yields no picture
        rc = new(data, 2, this._version == 1);
        this._Inter(rc);
        break;
      default:
        throw new NotSupportedException($"ScreenPressor frame type {type} is not one FFmpeg reads either.");
    }
    if (rc != null && rc.Left > 5)
      throw new InvalidDataException("This ScreenPressor frame has bytes left over after its picture.");
    (this._current, this._last) = (this._last, this._current);
    return true;
  }

  private (uint Colour, int Cx, int Cx1) _Units(ScprRangeDecoder rc, int cx, int cx1) {
    var first = rc.Unit(this._models.Pixel(0, cx + cx1), 400) & this._bits;
    cx1 = (cx << 6) & 0xFC0;
    cx = first >> this._shift;
    var second = rc.Unit(this._models.Pixel(1, cx + cx1), 400) & this._bits;
    cx1 = (cx << 6) & 0xFC0;
    cx = second >> this._shift;
    var third = rc.Unit(this._models.Pixel(2, cx + cx1), 400) & this._bits;
    cx1 = (cx << 6) & 0xFC0;
    cx = third >> this._shift;
    return ((uint)((third << 16) + (second << 8) + first), cx, cx1);
  }

  private (int Cx, int Cx1) _Context(uint colour)
    => bitsPerPixel == 16 ? ((int)((colour & 0x3FFFFF) >> 16), (int)((colour & 0x3F00) >> 2)) : ((int)((colour & 0xFFFFFF) >> 18), (int)((colour & 0xFC00) >> 4));

  private void _Intra(ScprRangeDecoder rc) {
    this._models.Reinitialise();
    var dst = this._current;
    int cx = 0, cx1 = 0, k = 0, x = 0, y = 0, lx = 0, ly = 0;
    uint colour;
    while (k < width + 1) {
      (colour, cx, cx1) = this._Units(rc, cx, cx1);
      var run = rc.Value(this._models.Run[0], 256, 400);
      if (run <= 0)
        throw new InvalidDataException("A ScreenPressor run is empty.");
      k += run;
      while (run-- > 0) {
        if (y >= height)
          throw new InvalidDataException("A ScreenPressor run passes the picture's end.");
        dst[y * width + x] = colour;
        lx = x;
        ly = y;
        if (++x >= width) {
          x = 0;
          ++y;
        }
      }
    }
    var ptype = 0;
    colour = 0;
    while (x < width && y < height) {
      ptype = rc.Value(this._models.Op[ptype], 6, 1000);
      if (ptype == 0)
        (colour, cx, cx1) = this._Units(rc, cx, cx1);
      var run = rc.Value(this._models.Run[ptype], 256, 400);
      if (run <= 0)
        throw new InvalidDataException("A ScreenPressor run is empty.");
      colour = ScprRuns.Intra(dst, width, height, ptype, run, ref x, ref y, colour, ref lx, ref ly);
      (cx, cx1) = this._Context(colour);
    }
  }

  private void _Inter(ScprRangeDecoder rc) {
    var models = this._models;
    var min = rc.Value(models.Range, 256, 1);
    min += rc.Value(models.Range, 256, 1) << 8;
    var max = rc.Value(models.Range, 256, 1);
    max += rc.Value(models.Range, 256, 1) << 8;
    var count = this._blocksWide * this._blocksHigh;
    if (min > max || min >= count)
      throw new InvalidDataException("This ScreenPressor frame's block range is out of order.");
    var blocks = new int[count];
    while (min <= max) {
      var fill = rc.Value(models.Fill, 5, 10);
      var n = rc.Value(models.Count, 256, 20);
      if (n <= 0)
        throw new InvalidDataException("A ScreenPressor block run is empty.");
      while (min < count && n-- > 0)
        blocks[min++] = fill;
    }

    Array.Copy(this._last, this._current, this._current.Length);
    var dst = this._current;
    var prev = this._last;
    int cx = 0, cx1 = 0;
    for (var by0 = 0; by0 < this._blocksHigh; ++by0)
      for (var bx0 = 0; bx0 < this._blocksWide; ++bx0) {
        var fill = blocks[by0 * this._blocksWide + bx0];
        if (fill == 0)
          continue;
        int sx1 = 0, sy1 = 0, sx2 = 16, sy2 = 16;
        if (((fill - 1) & 1) != 0) {
          sx1 = rc.Value(models.Sxy[0], 16, 100);
          sy1 = rc.Value(models.Sxy[1], 16, 100);
          sx2 = rc.Value(models.Sxy[2], 16, 100) + 1;
          sy2 = rc.Value(models.Sxy[3], 16, 100) + 1;
        }
        if (((fill - 1) & 2) != 0) {
          var mvx = rc.Value(models.Mv[0], 512, 100) - 256;
          var mvy = rc.Value(models.Mv[1], 512, 100) - 256;
          int top = by0 * 16, left = bx0 * 16;
          if (top + mvy + sy1 < 0 || left + mvx + sx1 < 0 || top + mvy + sy1 >= height || left + mvx + sx1 >= width)
            throw new InvalidDataException("A ScreenPressor motion vector points off the picture.");
          for (var i = 0; i < sy2 - sy1 && top + sy1 + i < height && top + mvy + sy1 + i < height; ++i)
            for (var j = 0; j < sx2 - sx1 && left + sx1 + j < width && left + mvx + sx1 + j < width; ++j)
              dst[(top + i + sy1) * width + left + sx1 + j] = prev[(top + mvy + sy1 + i) * width + left + sx1 + mvx + j];
        } else {
          int bx = bx0 * 16 + sx1, by = by0 * 16 + sy1;
          if (bx >= width)
            throw new InvalidDataException("A ScreenPressor block starts past the picture's edge.");
          var ptype = 0;
          uint colour = 0;
          while (by < by0 * 16 + sy2 && by < height) {
            ptype = rc.Value(models.Op[ptype], 6, 1000);
            if (ptype == 0)
              (colour, cx, cx1) = this._Units(rc, cx, cx1);
            var run = rc.Value(models.Run[ptype], 256, 400);
            if (run <= 0)
              throw new InvalidDataException("A ScreenPressor run is empty.");
            colour = ScprRuns.Inter(dst, prev, width, height, ptype, run, bx0, ref bx, ref by, colour, sx1, sx2);
            (cx, cx1) = this._Context(colour);
          }
        }
      }
  }
}

/// <summary>FFmpeg's <c>decode_run_i</c> and <c>decode_run_p</c> over a picture of rows without padding.</summary>
internal static class ScprRuns {

  private static uint _Gradient(uint up, uint left, uint upLeft) {
    int r = (int)(left & 0xFF) + (int)(up & 0xFF) - (int)(upLeft & 0xFF);
    int g = (int)((left >> 8) & 0xFF) + (int)((up >> 8) & 0xFF) - (int)((upLeft >> 8) & 0xFF);
    int b = (int)((left >> 16) & 0xFF) + (int)((up >> 16) & 0xFF) - (int)((upLeft >> 16) & 0xFF);
    return (uint)(((b & 0xFF) << 16) + ((g & 0xFF) << 8) + (r & 0xFF));
  }

  /// <summary>One intra run: a colour, the last pixel, the one above, a gradient, or the one above-left; returns the last colour written.</summary>
  internal static uint Intra(uint[] dst, int width, int height, int ptype, int run, ref int x, ref int y, uint colour, ref int lx, ref int ly) {
    if (y >= height)
      throw new InvalidDataException("A ScreenPressor run starts past the picture's end.");
    if (ptype is 2 && y < 1 || ptype is 4 or 5 && (y < 1 || (y == 1 && x == 0)))
      throw new InvalidDataException("A ScreenPressor run refers above the first row.");
    if (ptype == 3)
      throw new InvalidDataException("A ScreenPressor key frame names the previous picture.");
    while (run-- > 0) {
      var at = y * width + x;
      switch (ptype) {
        case 0:
          break;
        case 1:
          colour = dst[ly * width + lx];
          break;
        case 2:
          colour = dst[at - width];
          break;
        case 4:
          colour = _Gradient(dst[at - width], dst[ly * width + lx], dst[at - width - 1]);
          break;
        default:
          colour = dst[at - width - 1];
          break;
      }
      dst[at] = colour;
      lx = x;
      ly = y;
      if (++x >= width) {
        x = 0;
        ++y;
        if (y >= height && run > 0)
          throw new InvalidDataException("A ScreenPressor run passes the picture's end.");
      }
    }
    return ptype == 1 ? dst[ly * width + lx] : colour;
  }

  /// <summary>One run inside an inter frame's block: a colour, left, above, the last picture, a gradient, or above-left.</summary>
  internal static uint Inter(uint[] dst, uint[] prev, int width, int height, int ptype, int run, int blockX, ref int bx, ref int by, uint colour, int sx1, int sx2) {
    while (run-- > 0) {
      if (by >= height || (ptype is 2 or 4 or 5 && by < 1))
        throw new InvalidDataException("A ScreenPressor run lies outside the picture.");
      var at = by * width + bx;
      if (ptype is 1 or 4 or 5 && bx == 0 && by < (ptype == 1 ? 1 : 2))
        throw new InvalidDataException("A ScreenPressor run refers before the picture's start.");
      colour = ptype switch {
        0 => colour,
        1 => dst[at - 1],
        2 => dst[at - width],
        3 => prev[at],
        4 => _Gradient(dst[at - width], dst[at - 1], dst[at - width - 1]),
        _ => dst[at - width - 1],
      };
      dst[at] = colour;
      if (++bx >= blockX * 16 + sx2 || bx >= width) {
        bx = blockX * 16 + sx1;
        ++by;
      }
    }
    return colour;
  }
}

/// <summary>
/// Writes ScreenPressor's second generation: a key frame of runs, then inter frames that leave unchanged
/// 16x16 blocks alone and code the rest as runs of a colour, left, above, the last picture or above-left.
/// </summary>
internal sealed class ScprEncoder(int width, int height) {

  private readonly ScprModels _models = new();
  private uint[]? _last;
  private readonly int _blocksWide = (width + 15) / 16;
  private readonly int _blocksHigh = (height + 15) / 16;

  private static (int Cx, int Cx1) _Context(uint colour) => ((int)((colour & 0xFFFFFF) >> 18), (int)((colour & 0xFC00) >> 4));

  private void _Units(ScprRangeEncoder rc, uint colour, ref int cx, ref int cx1) {
    int first = (int)(colour & 0xFF), second = (int)((colour >> 8) & 0xFF), third = (int)((colour >> 16) & 0xFF);
    rc.Unit(this._models.Pixel(0, cx + cx1), first, 400);
    cx1 = (cx << 6) & 0xFC0;
    cx = first >> 2;
    rc.Unit(this._models.Pixel(1, cx + cx1), second, 400);
    cx1 = (cx << 6) & 0xFC0;
    cx = second >> 2;
    rc.Unit(this._models.Pixel(2, cx + cx1), third, 400);
    cx1 = (cx << 6) & 0xFC0;
    cx = third >> 2;
  }

  /// <summary>One frame from pixels <c>(third &lt;&lt; 16) | (second &lt;&lt; 8) | first</c>, in coded row order.</summary>
  internal byte[] Encode(uint[] pixels) {
    var output = new List<byte>();
    if (this._last == null) {
      output.AddRange([18, 0]);
      var rc = new ScprRangeEncoder(output);
      this._Intra(rc, pixels);
      rc.Finish();
    } else {
      output.AddRange([1, 0]);
      var rc = new ScprRangeEncoder(output);
      this._Inter(rc, pixels, this._last);
      rc.Finish();
    }
    this._last = (uint[])pixels.Clone();
    return [.. output];
  }

  private void _Intra(ScprRangeEncoder rc, uint[] pixels) {
    this._models.Reinitialise();
    int cx = 0, cx1 = 0, k = 0, at = 0;
    while (k < width + 1) {
      var colour = pixels[at];
      this._Units(rc, colour, ref cx, ref cx1);
      var run = 1;
      while (at + run < pixels.Length && pixels[at + run] == colour && run < 255)
        ++run;
      rc.Value(this._models.Run[0], 256, run, 400);
      k += run;
      at += run;
    }
    var ptype = 0;
    while (at < pixels.Length) {
      var (next, run) = _BestIntra(pixels, at);
      rc.Value(this._models.Op[ptype], 6, next, 1000);
      ptype = next;
      if (ptype == 0)
        this._Units(rc, pixels[at], ref cx, ref cx1);
      rc.Value(this._models.Run[ptype], 256, run, 400);
      at += run;
      (cx, cx1) = _Context(pixels[at - 1]);
    }
  }

  /// <summary>The longest run any copying operation reproduces here, or a run of one colour.</summary>
  private (int Ptype, int Run) _BestIntra(uint[] pixels, int at) {
    int bestType = 0, bestRun = 0;
    for (var ptype = 1; ptype <= 5; ++ptype) {
      if (ptype == 3)
        continue;
      var run = 0;
      while (at + run < pixels.Length && run < 255) {
        var p = at + run;
        int y = p / width, x = p % width;
        if (ptype is 2 && y < 1 || ptype is 4 or 5 && (y < 1 || (y == 1 && x == 0)))
          break;
        var predicted = ptype switch {
          1 => pixels[p - 1],
          2 => pixels[p - width],
          4 => _Gradient(pixels[p - width], pixels[p - 1], pixels[p - width - 1]),
          _ => pixels[p - width - 1],
        };
        if (predicted != pixels[p])
          break;
        ++run;
      }
      if (run > bestRun)
        (bestType, bestRun) = (ptype, run);
    }
    if (bestRun > 0)
      return (bestType, bestRun);
    var same = 1;
    while (at + same < pixels.Length && pixels[at + same] == pixels[at] && same < 255)
      ++same;
    return (0, same);
  }

  private static uint _Gradient(uint up, uint left, uint upLeft) {
    int r = (int)(left & 0xFF) + (int)(up & 0xFF) - (int)(upLeft & 0xFF);
    int g = (int)((left >> 8) & 0xFF) + (int)((up >> 8) & 0xFF) - (int)((upLeft >> 8) & 0xFF);
    int b = (int)((left >> 16) & 0xFF) + (int)((up >> 16) & 0xFF) - (int)((upLeft >> 16) & 0xFF);
    return (uint)(((b & 0xFF) << 16) + ((g & 0xFF) << 8) + (r & 0xFF));
  }

  private void _Inter(ScprRangeEncoder rc, uint[] pixels, uint[] prev) {
    var models = this._models;
    var count = this._blocksWide * this._blocksHigh;
    var changed = new bool[count];
    int first = -1, last = -1;
    for (var b = 0; b < count; ++b) {
      int left = b % this._blocksWide * 16, top = b / this._blocksWide * 16;
      for (var y = top; y < Math.Min(top + 16, height) && !changed[b]; ++y)
        for (var x = left; x < Math.Min(left + 16, width); ++x)
          if (pixels[y * width + x] != prev[y * width + x]) {
            changed[b] = true;
            break;
          }
      if (changed[b]) {
        if (first < 0)
          first = b;
        last = b;
      }
    }
    if (first < 0)
      first = last = 0; // a frame that changes nothing still names one block, left alone

    rc.Value(models.Range, 256, first & 0xFF, 1);
    rc.Value(models.Range, 256, first >> 8, 1);
    rc.Value(models.Range, 256, last & 0xFF, 1);
    rc.Value(models.Range, 256, last >> 8, 1);
    for (var b = first; b <= last;) {
      var fill = changed[b] ? 1 : 0;
      var n = 1;
      while (b + n <= last && (changed[b + n] ? 1 : 0) == fill && n < 255)
        ++n;
      rc.Value(models.Fill, 5, fill, 10);
      rc.Value(models.Count, 256, n, 20);
      b += n;
    }

    var dst = (uint[])prev.Clone();
    int cx = 0, cx1 = 0;
    for (var b = first; b <= last; ++b) {
      if (!changed[b])
        continue;
      int bx0 = b % this._blocksWide, by0 = b / this._blocksWide;
      int bx = bx0 * 16, by = by0 * 16;
      var ptype = 0;
      while (by < by0 * 16 + 16 && by < height) {
        var (next, run) = this._BestInter(pixels, dst, prev, bx0, bx, by);
        rc.Value(models.Op[ptype], 6, next, 1000);
        ptype = next;
        if (ptype == 0)
          this._Units(rc, pixels[by * width + bx], ref cx, ref cx1);
        rc.Value(models.Run[ptype], 256, run, 400);
        var colour = ScprRuns.Inter(dst, prev, width, height, ptype, run, bx0, ref bx, ref by, pixels[by * width + bx], 0, 16);
        (cx, cx1) = _Context(colour);
      }
    }
  }

  /// <summary>Inside a block, the longest run a copying operation reproduces from here, or a run of one colour.</summary>
  private (int Ptype, int Run) _BestInter(uint[] pixels, uint[] dst, uint[] prev, int blockX, int bx, int by) {
    int bestType = 0, bestRun = 0;
    var right = Math.Min(blockX * 16 + 16, width);
    for (var ptype = 1; ptype <= 5; ++ptype) {
      int x = bx, y = by, run = 0;
      while (run < 255 && y < Math.Min((by / 16) * 16 + 16, height)) {
        if (ptype is 2 or 4 or 5 && y < 1 || ptype is 1 or 4 or 5 && x == 0 && y < (ptype == 1 ? 1 : 2))
          break;
        var at = y * width + x;
        // Within the run, pixels already passed hold their new values, as they will in the decoder.
        uint Here(int p) => p >= by * width + bx && _InBlock(p, blockX, bx, by) ? pixels[p] : dst[p];
        var predicted = ptype switch {
          1 => Here(at - 1),
          2 => Here(at - width),
          3 => prev[at],
          4 => _Gradient(Here(at - width), Here(at - 1), Here(at - width - 1)),
          _ => Here(at - width - 1),
        };
        if (predicted != pixels[at])
          break;
        ++run;
        if (++x >= right) {
          x = blockX * 16;
          ++y;
        }
      }
      if (run > bestRun)
        (bestType, bestRun) = (ptype, run);
    }
    if (bestRun > 0)
      return (bestType, bestRun);
    {
      int x = bx, y = by, run = 0;
      var colour = pixels[by * width + bx];
      while (run < 255 && y < Math.Min((by / 16) * 16 + 16, height) && pixels[y * width + x] == colour) {
        ++run;
        if (++x >= right) {
          x = blockX * 16;
          ++y;
        }
      }
      return (0, run);
    }
  }

  /// <summary>Whether a position lies in the block and at or after where the current run started.</summary>
  private bool _InBlock(int p, int blockX, int bx, int by) {
    int y = p / width, x = p % width;
    return x >= blockX * 16 && x < blockX * 16 + 16 && y >= (by / 16) * 16 && (y > by || (y == by && x >= bx));
  }
}
