using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Scpr;

/// <summary>
/// One of ScreenPressor 3's fixed-alphabet adaptive tables (<c>RunModel3</c>, <c>OpModel3</c> and the
/// rest): a width and a start per symbol in a 4096-wide code space, counts that grow by sixteen, and a
/// 32-entry lookup by the code's top five bits.
/// </summary>
internal sealed class Scpr3Table {
  internal readonly int Symbols;
  internal uint CountSum;
  internal readonly ushort[] Widths;
  internal readonly ushort[] Starts;
  internal readonly ushort[] Counts;
  internal readonly byte[] Lookup = new byte[32];

  internal Scpr3Table(int symbols) {
    this.Symbols = symbols;
    this.Widths = new ushort[symbols];
    this.Starts = new ushort[symbols];
    this.Counts = new ushort[symbols];
  }

  /// <summary><c>renew_table3</c>.</summary>
  internal void Renew() {
    uint a = 0, b = 4096u / (uint)this.Symbols, c = b - (b >> 1);
    this.CountSum = c * (uint)this.Symbols;
    Array.Clear(this.Lookup);
    for (var d = 0; d < this.Symbols; ++d) {
      this.Widths[d] = (ushort)b;
      this.Starts[d] = (ushort)a;
      this.Counts[d] = (ushort)c;
      for (int q = (int)((a + 128 - 1) >> 7), f = (int)((a + b - 1) >> 7) + 1; q < f; ++q)
        this.Lookup[q] = (byte)d;
      a += b;
    }
  }

  internal int Decode(uint slot, out uint width, out uint start)
    => Scpr3Coding.DecodeValue(slot, this.Symbols - 1, ref this.CountSum, this.Widths, this.Starts, this.Counts, this.Lookup, out width, out start);
}

/// <summary>
/// ScreenPressor 3's per-context colour model (<c>PixelModel3</c>), which changes shape as it learns:
/// types 0 to 3 take the component as a raw byte while they collect the symbols seen, 4 and 5 code a
/// few symbols adaptively with the rest spread evenly between them, 6 keeps up to 64 symbols in
/// frequency order with scaled shares, and 7 is a full 256-symbol table.
/// </summary>
internal sealed class Scpr3PixelModel {
  internal byte Type;
  internal byte Length;
  internal byte MaxPos;
  internal byte FShift;
  internal ushort Size;
  internal uint CountSum;
  internal byte[] Symbols = new byte[256];
  internal ushort[] Freqs = new ushort[256];
  internal ushort[] Freqs1 = new ushort[256];
  internal ushort[] Counts = new ushort[256];
  internal byte[] Lookup = new byte[32];

  internal bool IsLiteral => this.Type <= 3;

  private void _CopyFrom(Scpr3PixelModel n) {
    this.Type = n.Type;
    this.Length = n.Length;
    this.MaxPos = n.MaxPos;
    this.FShift = n.FShift;
    this.Size = n.Size;
    this.CountSum = n.CountSum;
    this.Symbols = n.Symbols;
    this.Freqs = n.Freqs;
    this.Freqs1 = n.Freqs1;
    this.Counts = n.Counts;
    this.Lookup = n.Lookup;
  }

  // ==========================================================================================
  // Types 0 to 3: the raw byte, and what seeing it does to the model
  // ==========================================================================================

  /// <summary>The model updates of <c>decode_unit3</c> for a component that came as a raw byte.</summary>
  internal void Literal(int value) {
    switch (this.Type) {
      case 0:
        this.Type = 1;
        this.Size = 1;
        this.Symbols[0] = (byte)value;
        break;
      case 1:
        this._Static1(value);
        break;
      case 2:
        this._Static2(value);
        break;
      default:
        this._Static3(value);
        break;
    }
  }

  private void _Static1(int value) {
    int size = this.Size;
    for (var i = 0; i < size; ++i)
      if (value == this.Symbols[i]) {
        if (size <= 4)
          this._Model1To4(value);
        else
          this._Model1To5(value);
        return;
      }

    if (size >= 14) {
      this._Model1To2(value);
      return;
    }

    this.Symbols[size] = (byte)value;
    ++this.Size;
  }

  private void _Model1To2(int value) {
    var n = new Scpr3PixelModel { Type = 2, Size = (ushort)(this.Size + 1) };
    Array.Copy(this.Symbols, n.Symbols, this.Size);
    n.Symbols[this.Size] = (byte)value;
    this._CopyFrom(n);
  }

  private void _Model1To4(int value) {
    var n = new Scpr3PixelModel { Type = 4, Size = this.Size };
    Array.Copy(this.Symbols, n.Symbols, n.Size);
    Array.Sort(n.Symbols, 0, n.Size);
    for (var i = 0; i < n.Size; ++i)
      if (value == n.Symbols[i]) {
        n.Freqs[i] = 100;
        n.MaxPos = (byte)i;
      } else {
        n.Freqs[i] = 50;
      }
    this._CopyFrom(n);
  }

  private void _Model1To5(int value) {
    this._Model1To4(value);
    uint a = 256u - this.Size;
    for (var i = 0; i < this.Size; ++i)
      a += this.Freqs[i];
    this.Type = 5;
    this.CountSum = a;
  }

  private void _Static2(int value) {
    int size = this.Size;
    for (var i = 0; i < size; ++i)
      if (value == this.Symbols[i]) {
        this._Model2To6(value, this.Size <= 32 ? 32 : 64);
        return;
      }

    if (size >= 64) {
      this._Model2To3(value);
      return;
    }

    this.Symbols[size] = (byte)value;
    ++this.Size;
  }

  private void _Model2To6(int value, int a4) {
    var n = new Scpr3PixelModel { Type = 6, Length = (byte)a4 };
    Array.Fill(n.Symbols, (byte)1, 0, a4);
    int c = this.Size;
    var d = 256 - c + (64 * c + 64);
    int e;
    for (e = 0; d <= 2048; ++e)
      d <<= 1;

    int g = 0, q = 0;
    Array.Sort(this.Symbols, 0, c);
    d = 0;
    for (var f = 0; f < c; ++f) {
      var p = f;
      int k = this.Symbols[p];
      g += k - q;
      if (k == value) {
        d = p;
        q = 128;
      } else {
        q = 64;
      }
      var l = q << e;
      n.Freqs[2 * p] = (ushort)l;
      n.Freqs[2 * p + 1] = (ushort)(g << e);
      n.Symbols[p] = (byte)k;
      n.Counts[p] = (ushort)(l - (l >> 1));
      g += q;
      q = k + 1;
    }

    n.Size = (ushort)c;
    n.FShift = (byte)e;
    n._CalcSum();
    if (d > 0) {
      var c0 = n.Freqs[0];
      var e0 = n.Freqs[1];
      var g0 = n.Freqs[2 * d + 1];
      n.Freqs[0] = n.Freqs[2 * d];
      n.Freqs[1] = g0;
      n.Freqs[2 * d] = c0;
      n.Freqs[2 * d + 1] = e0;
      (n.Counts[0], n.Counts[d]) = (n.Counts[d], n.Counts[0]);
      (n.Symbols[0], n.Symbols[d]) = (n.Symbols[d], n.Symbols[0]);
    }

    this._CopyFrom(n);
  }

  private void _Model2To3(int value) {
    var n = new Scpr3PixelModel { Type = 3, Size = (ushort)(this.Size + 1) };
    Array.Copy(this.Symbols, n.Symbols, this.Size);
    n.Symbols[this.Size] = (byte)value;
    this._CopyFrom(n);
  }

  private void _Static3(int value) {
    int size = this.Size;
    for (var i = 0; i < size; ++i)
      if (value == this.Symbols[i]) {
        this._Model3To7(value);
        return;
      }

    if (size >= 256)
      return;
    this.Symbols[size] = (byte)value;
    ++this.Size;
  }

  private void _Model3To7(int value) {
    var n = new Scpr3PixelModel { Type = 7 };
    for (var c = 0; c < 256; ++c) {
      n.Freqs[c] = 1;
      n.Counts[c] = 1;
    }

    int size = this.Size;
    var d = (4096 - (256 - size)) / (size + 1);
    var e = d - (d >> 1);
    for (var g = 0; g < size; ++g) {
      int q = this.Symbols[g];
      n.Freqs[q] = (ushort)d;
      n.Counts[q] = (ushort)e;
    }

    n.Freqs[value] += (ushort)d;
    n.Counts[value] += 16;
    n.CountSum = 0;
    for (int s = 0, cum = 0; s < 256; ++s) {
      n.CountSum += n.Counts[s];
      n.Freqs1[s] = (ushort)cum;
      int width = n.Freqs[s];
      var f = ((cum + width - 1) >> 7) + 1;
      if (f > n.Lookup.Length)
        throw new InvalidDataException("A ScreenPressor 3 colour model outgrows its lookup.");
      for (var q = (cum + 128 - 1) >> 7; q < f; ++q)
        n.Lookup[q] = (byte)s;
      cum += width;
    }

    this._CopyFrom(n);
  }

  // ==========================================================================================
  // Types 4 to 7: coded components
  // ==========================================================================================

  /// <summary>
  /// <c>decode_unit3</c> for a coded component: the symbol at <paramref name="slot"/> of the 4096-wide
  /// code space, its interval, and the model's update for it.
  /// </summary>
  internal int Decode(uint slot, out uint width, out uint start) {
    int value;
    switch (this.Type) {
      case 4: {
        uint total = (uint)(this.Freqs[0] + this.Freqs[1] + this.Freqs[2] + this.Freqs[3] + 256 - this.Size);
        if (!this._Adaptive45((int)slot, out value, out width, out start, ref total, 4))
          this._Model4To5(value);
        break;
      }
      case 5:
        if (!this._Adaptive45((int)slot, out value, out width, out start, ref this.CountSum, 16))
          this._Model5To6((byte)value);
        break;
      case 6:
        if (!this._Adaptive6(slot, out value, out width, out start))
          this._Model6To7();
        break;
      case 7:
        value = Scpr3Coding.DecodeValue(slot, 255, ref this.CountSum, this.Freqs, this.Freqs1, this.Counts, this.Lookup, out width, out start);
        break;
      default:
        throw new InvalidOperationException("A raw-byte colour model has no coded symbol.");
    }

    if (value is < 0 or > 255)
      throw new InvalidDataException("A ScreenPressor 3 colour component decodes outside a byte.");
    return value;
  }

  /// <summary>Where in the code space <see cref="Decode"/> finds <paramref name="value"/>: the start of its interval.</summary>
  internal uint SlotOf(int value) {
    switch (this.Type) {
      case 4:
      case 5: {
        uint e = this.Type == 4 ? (uint)(this.Freqs[0] + this.Freqs[1] + this.Freqs[2] + this.Freqs[3] + 256 - this.Size) : this.CountSum;
        int d;
        for (d = 0; e <= 2048; ++d)
          e <<= 1;
        var extra = (ushort)((4096 - e) >> d);
        uint position = 0, g = 0;
        for (var q = 0; q < this.Size; ++q) {
          uint f = this.Symbols[q];
          uint k = q == this.MaxPos ? (ushort)(this.Freqs[q] + extra) : this.Freqs[q];
          if (value < f)
            return (position + (uint)value - g) << d;
          if (value == f)
            return (position + f - g) << d;
          position += f - g + k;
          g = f + 1;
        }

        return (position + (uint)value - g) << d;
      }
      case 6: {
        int below = -1, belowAt = -1;
        for (var i = 0; i < this.Size; ++i) {
          if (this.Symbols[i] == value)
            return this.Freqs[2 * i + 1];
          if (this.Symbols[i] < value && this.Symbols[i] > below) {
            below = this.Symbols[i];
            belowAt = i;
          }
        }

        return belowAt < 0
          ? (uint)value << this.FShift
          : (uint)(this.Freqs[2 * belowAt + 1] + this.Freqs[2 * belowAt] + ((value - below - 1) << this.FShift));
      }
      default:
        return this.Freqs1[value];
    }
  }

  /// <summary><c>decode_adaptive45</c>.</summary>
  private bool _Adaptive45(int code, out int value, out uint width, out uint start, ref uint c, int max) {
    uint e = c, totfr = c;
    int d;
    for (d = 0; e <= 2048; ++d)
      e <<= 1;
    int maxpos = this.MaxPos;
    code >>= d;
    c = this.Freqs[maxpos];
    this.Freqs[maxpos] = (ushort)(this.Freqs[maxpos] + ((4096 - e) >> d));

    uint g = 0;
    e = 0;
    int q;
    for (q = 0; q < this.Size; ++q) {
      uint f = this.Symbols[q];
      var p = e + f - g;
      uint k = this.Freqs[q];
      if ((uint)code < p) {
        value = (int)((uint)code - e + g);
        start = (ushort)(code << d);
        width = (ushort)(1 << d);
        this.Freqs[maxpos] = (ushort)c;
        var added = this._AddSymbol(q, (uint)value, ref totfr, max);
        c = totfr;
        return added;
      }

      if (p + k > (uint)code) {
        value = (int)f;
        e += f - g;
        start = (ushort)(e << d);
        width = (ushort)(k << d);
        this.Freqs[maxpos] = (ushort)c;
        this.Freqs[q] += 50;
        totfr += 50;
        if (q != maxpos && this.Freqs[q] > this.Freqs[maxpos])
          this.MaxPos = (byte)q;
        if (totfr + 50 > 4096)
          this._Rescale(ref totfr);
        c = totfr;
        return true;
      }

      e += f - g + k;
      g = f + 1;
    }

    this.Freqs[maxpos] = (ushort)c;
    value = (int)(g + (uint)code - e);
    start = (ushort)(code << d);
    width = (ushort)(1 << d);
    var ok = this._AddSymbol(q, (uint)value, ref totfr, max);
    c = totfr;
    return ok;
  }

  /// <summary><c>add_symbol</c>.</summary>
  private bool _AddSymbol(int index, uint symbol, ref uint totfr, int max) {
    if (this.Size == max)
      return false;
    for (var c = this.Size - 1; c >= index; --c) {
      this.Symbols[c + 1] = this.Symbols[c];
      this.Freqs[c + 1] = this.Freqs[c];
    }

    this.Symbols[index] = (byte)symbol;
    this.Freqs[index] = 50;
    ++this.Size;
    if (this.MaxPos >= index)
      ++this.MaxPos;
    totfr += 50;
    if (totfr + 50 > 4096)
      this._Rescale(ref totfr);
    return true;
  }

  /// <summary><c>rescale</c>.</summary>
  private void _Rescale(ref uint totfr) {
    uint a = 256u - this.Size;
    for (var b = 0; b < this.Size; ++b) {
      this.Freqs[b] -= (ushort)(this.Freqs[b] >> 1);
      a += this.Freqs[b];
    }

    totfr = a;
  }

  /// <summary><c>update_model4_to_5</c>.</summary>
  private void _Model4To5(int value) {
    var n = new Scpr3PixelModel { Type = 5 };
    int c, e = 0, g;
    for (c = 0; c < this.Size && this.Symbols[c] < value; ++c) {
      n.Symbols[c] = this.Symbols[c];
      e += n.Freqs[c] = this.Freqs[c];
    }

    g = c;
    n.Symbols[g] = (byte)value;
    e += n.Freqs[g++] = 50;
    for (; c < this.Size; ++g, ++c) {
      n.Symbols[g] = this.Symbols[c];
      e += n.Freqs[g] = this.Freqs[c];
    }

    n.Size = (ushort)(this.Size + 1);
    if (e > 4096) {
      uint ignored = 0;
      n._Rescale(ref ignored);
    }

    uint a = 256u - n.Size;
    for (var b = 0; b < n.Size; ++b)
      a += n.Freqs[b];
    n.CountSum = a;
    this._CopyFrom(n);
  }

  /// <summary><c>calc_sum</c>.</summary>
  private void _CalcSum() {
    int len = this.Length;
    var a = (uint)(256 - this.Size) << (this.FShift > 0 ? this.FShift - 1 : 0);
    for (var c = 0; c < len; ++c)
      a += this.Counts[c];
    this.Counts[len] = (ushort)a;
  }

  /// <summary><c>rescale_dec</c>.</summary>
  private void _RescaleDec() {
    var counts = new ushort[256];
    var freqs = new ushort[512];
    counts.AsSpan().Fill((ushort)(1u << (0 < this.FShift ? this.FShift - 1 : 0)));
    for (var i = 0; i < this.Size; ++i)
      counts[this.Symbols[i]] = this.Counts[i];

    uint a = 0;
    for (var b = 0; b < 256; ++b) {
      freqs[2 * b] = counts[b];
      freqs[2 * b + 1] = (ushort)a;
      a += counts[b];
    }

    if (this.FShift > 0)
      --this.FShift;

    a = (uint)(256 - this.Size) << (0 < this.FShift ? this.FShift - 1 : 0);
    for (var b = 0; b < this.Size; ++b) {
      this.Counts[b] -= (ushort)(this.Counts[b] >> 1);
      a += this.Counts[b];
      int e = this.Symbols[b];
      var g = freqs[2 * e + 1];
      this.Freqs[2 * b] = freqs[2 * e];
      this.Freqs[2 * b + 1] = g;
    }

    this.Counts[this.Length] = (ushort)a;
  }

  /// <summary><c>update_model5_to_6</c>.</summary>
  private void _Model5To6(byte value) {
    var n = new Scpr3PixelModel { Type = 6, Length = 32 };
    int c = this.Size, d = 256 - c, e;
    for (e = 0; e < c; ++e)
      d += this.Freqs[e];
    for (e = 0; d <= 2048; ++e)
      d <<= 1;

    int q = 0, g, k, p, f;
    d = 0;
    for (g = 0; g < c; ++g) {
      p = this.Symbols[g];
      d += p - q;
      q = this.Freqs[g];
      k = q << e;
      n.Freqs[2 * g] = (ushort)k;
      n.Freqs[2 * g + 1] = (ushort)(d << e);
      n.Counts[g] = (ushort)(k - (k >> 1));
      n.Symbols[g] = (byte)p;
      d += q;
      q = p + 1;
    }

    n.FShift = (byte)e;
    e = 1 << n.FShift;
    d = 0;
    if (value > 0) {
      d = -1;
      f = g = 0;
      for (p = 0; p < c; ++p) {
        k = n.Symbols[p];
        if (k > d && k < value) {
          d = k;
          g = n.Freqs[2 * p];
          f = n.Freqs[2 * p + 1];
        }
      }

      d = 0 < g ? f + g + ((value - d - 1) << n.FShift) : value << n.FShift;
    }

    n.Freqs[2 * c] = (ushort)e;
    n.Freqs[2 * c + 1] = (ushort)d;
    n.Counts[c] = (ushort)(e - (e >> 1));
    n.Symbols[c] = value;
    n.Size = (ushort)(c + 1);
    e = 25 << n.FShift;
    n.Counts[c] += (ushort)e;
    n.Counts[32] += (ushort)e;
    if (n.Counts[32] + e > 4096)
      n._RescaleDec();

    n._CalcSum();
    for (c = 0, e = n.Size - 1; c < e; ++c)
      for (g = c + 1, f = n.Size; g < f; ++g) {
        q = n.Freqs[2 * g];
        k = n.Freqs[2 * c];
        if (q > k) {
          var l = n.Freqs[2 * c + 1];
          var h = n.Freqs[2 * g + 1];
          n.Freqs[2 * c] = (ushort)q;
          n.Freqs[2 * c + 1] = h;
          n.Freqs[2 * g] = (ushort)k;
          n.Freqs[2 * g + 1] = l;
          (n.Counts[c], n.Counts[g]) = (n.Counts[g], n.Counts[c]);
          (n.Symbols[c], n.Symbols[g]) = (n.Symbols[g], n.Symbols[c]);
        }
      }

    this._CopyFrom(n);
  }

  /// <summary><c>grow_dec</c>.</summary>
  private void _Grow() {
    var a = 2 * this.Length;
    this.Counts[2 * this.Length] = this.Counts[this.Length];
    this.Length = (byte)a;
  }

  /// <summary><c>add_dec</c>.</summary>
  private int _Add(int symbol, int f1, int f2) {
    if (this.Size >= 40 || this.Size >= this.Length)
      return -1;
    int size = this.Size;
    this.Symbols[size] = (byte)symbol;
    this.Freqs[2 * size] = (ushort)f1;
    this.Freqs[2 * size + 1] = (ushort)f2;
    this.Counts[size] = (ushort)(f1 - (f1 >> 1));
    ++this.Size;
    return size;
  }

  /// <summary><c>incr_cntdec</c>.</summary>
  private void _IncrementCount(int a) {
    var b = 25 << this.FShift;
    int len = this.Length;
    this.Counts[a] += (ushort)b;
    this.Counts[len] += (ushort)b;
    if (a > 0 && this.Counts[a] > this.Counts[a - 1]) {
      (this.Counts[a], this.Counts[a - 1]) = (this.Counts[a - 1], this.Counts[a]);
      var d = this.Freqs[2 * a];
      var e = this.Freqs[2 * a + 1];
      var g = this.Freqs[2 * (a - 1) + 1];
      this.Freqs[2 * a] = this.Freqs[2 * (a - 1)];
      this.Freqs[2 * a + 1] = g;
      this.Freqs[2 * (a - 1)] = d;
      this.Freqs[2 * (a - 1) + 1] = e;
      (this.Symbols[a], this.Symbols[a - 1]) = (this.Symbols[a - 1], this.Symbols[a]);
    }

    if (this.Counts[len] + b > 4096)
      this._RescaleDec();
  }

  /// <summary><c>decode_adaptive6</c>; false where the model has to become type 7.</summary>
  private bool _Adaptive6(uint code, out int value, out uint width, out uint start) {
    int c = 0, d = 0, e = 0, g;
    for (g = 0; g < this.Size; ++g) {
      uint p = this.Freqs[2 * g + 1];
      if (p <= code) {
        uint k = this.Freqs[2 * g];
        if (p + k > code) {
          value = this.Symbols[g];
          width = k;
          start = p;
          this._IncrementCount(g);
          return true;
        }

        if (p >= d) {
          c = (int)k;
          d = (int)p;
          e = this.Symbols[g];
        }
      }
    }

    g = 1 << this.FShift;
    int q, f;
    if (c > 0) {
      f = (int)((code - (uint)(d + c)) >> this.FShift);
      q = f + e + 1;
      f = d + c + (f << this.FShift);
    } else {
      q = (int)(code >> this.FShift);
      f = q << this.FShift;
    }

    width = (ushort)g;
    start = (ushort)f;
    value = q;

    c = this._Add(q, g, f);
    if (c < 0) {
      if (this.Length == 64)
        return false;
      this._Grow();
      c = this._Add(q, g, f);
      if (c < 0)
        throw new InvalidDataException("A ScreenPressor 3 colour model cannot take another symbol.");
    }

    this._IncrementCount(c);
    return true;
  }

  /// <summary><c>update_model6_to_7</c>.</summary>
  private void _Model6To7() {
    var n = new Scpr3PixelModel { Type = 7 };
    int length = this.Length;
    n.CountSum = this.Counts[length];
    for (var i = 0; i < length; ++i) {
      if (this.Counts[i] == 0)
        continue;
      int index = this.Symbols[i];
      n.Freqs[index] = this.Freqs[2 * i];
      n.Freqs1[index] = this.Freqs[2 * i + 1];
      n.Counts[index] = this.Counts[i];
    }

    var c = 1 << this.FShift;
    var d = c - (c >> 1);
    for (int j = 0, e = 0; j < 256; ++j) {
      int f = n.Freqs[j];
      if (f == 0) {
        f = c;
        n.Freqs[j] = (ushort)c;
        n.Freqs1[j] = (ushort)e;
        n.Counts[j] = (ushort)d;
      }

      var p = (e + 127) >> 7;
      var k = ((f + e - 1) >> 7) + 1;
      if (k > n.Lookup.Length)
        throw new InvalidDataException("A ScreenPressor 3 colour model outgrows its lookup.");
      for (var i = 0; i < k - p; ++i)
        n.Lookup[p + i] = (byte)j;
      e += f;
    }

    this._CopyFrom(n);
  }
}

/// <summary>Every adaptive model a ScreenPressor 3 frame is coded with.</summary>
internal sealed class Scpr3Models {
  private readonly Scpr3PixelModel[] _pixel = new Scpr3PixelModel[3 * 4096];
  internal readonly Scpr3Table[] Run = [.. _Make(6, 256)];
  internal readonly Scpr3Table Range = new(256);
  internal readonly Scpr3Table Fill = new(5);
  internal readonly Scpr3Table Count = new(256);
  internal readonly Scpr3Table[] Sxy = [.. _Make(4, 16)];
  internal readonly Scpr3Table[] Mv = [.. _Make(2, 512)];
  internal readonly Scpr3Table[] Op = [.. _Make(6, 6)];

  private static IEnumerable<Scpr3Table> _Make(int count, int symbols) {
    for (var i = 0; i < count; ++i)
      yield return new(symbols);
  }

  internal Scpr3Models() {
    for (var i = 0; i < this._pixel.Length; ++i)
      this._pixel[i] = new();
  }

  internal Scpr3PixelModel Pixel(int component, int context) => this._pixel[component * 4096 + context];

  /// <summary><c>reinit_tables3</c>: every colour model back to type 0, every table back to even shares.</summary>
  internal void Reinitialise() {
    foreach (var model in this._pixel)
      model.Type = 0;
    foreach (var t in this.Run)
      t.Renew();
    this.Range.Renew();
    this.Fill.Renew();
    this.Count.Renew();
    foreach (var t in this.Sxy)
      t.Renew();
    foreach (var t in this.Mv)
      t.Renew();
    foreach (var t in this.Op)
      t.Renew();
  }
}

internal static class Scpr3Coding {

  /// <summary><c>decode_value3</c> without its reading: the symbol at <paramref name="slot"/>, and the table's update.</summary>
  internal static int DecodeValue(uint slot, int max, ref uint countSum, ushort[] widths, ushort[] starts, ushort[] counts, byte[] lookup, out uint width, out uint start) {
    var code = slot & 0xFFF;
    int r = lookup[code >> 7];
    if (r < max)
      while (starts[r + 1] <= code)
        if (++r >= max)
          break;
    if (r > max)
      throw new InvalidDataException("A ScreenPressor 3 symbol decodes past its table.");

    counts[r] += 16;
    width = widths[r];
    start = starts[r];
    countSum += 16;
    if (countSum + 16 > 4096) {
      countSum = 0;
      for (int c = 0, i = 0; i < max + 1; ++i) {
        int e = counts[i];
        starts[i] = (ushort)c;
        widths[i] = (ushort)e;
        var g = (c + 127) >> 7;
        c += e;
        var q = ((c - 1) >> 7) + 1;
        if (q > lookup.Length)
          throw new InvalidDataException("A ScreenPressor 3 table outgrows its lookup.");
        for (var j = 0; j < q - g; ++j)
          lookup[j + g] = (byte)i;
        var y = e - (e >> 1);
        counts[i] = (ushort)y;
        countSum += (uint)y;
      }
    }

    return r;
  }

  /// <summary>One colour component through its model, read.</summary>
  internal static int Unit(Scpr3RangeDecoder rc, Scpr3PixelModel model) {
    if (model.IsLiteral) {
      var value = rc.Literal();
      model.Literal(value);
      return value;
    }

    var decoded = model.Decode(rc.Slot, out var width, out var start);
    rc.Consume(width, start);
    return decoded;
  }

  /// <summary>One colour component through its model, written; the model learns exactly as the reader's does.</summary>
  internal static void Unit(Scpr3SymbolWriter rc, Scpr3PixelModel model, int value) {
    if (model.IsLiteral) {
      rc.Literal((byte)value);
      model.Literal(value);
      return;
    }

    var decoded = model.Decode(model.SlotOf(value), out var width, out var start);
    if (decoded != value)
      throw new InvalidOperationException($"A ScreenPressor 3 colour model of type {model.Type} cannot state {value}.");
    rc.Symbol(width, start);
  }

  internal static int Value(Scpr3RangeDecoder rc, Scpr3Table table) {
    var value = table.Decode(rc.Slot, out var width, out var start);
    rc.Consume(width, start);
    return value;
  }

  internal static void Value(Scpr3SymbolWriter rc, Scpr3Table table, int value) {
    var decoded = table.Decode(table.Starts[value], out var width, out var start);
    if (decoded != value)
      throw new InvalidOperationException($"A ScreenPressor 3 table cannot state {value}.");
    rc.Symbol(width, start);
  }
}

/// <summary>
/// ScreenPressor 3's coder: a byte-wise rANS over a 4096-wide code space (<c>decode3</c>) whose state
/// is renormalised to stay at or above 2^23, with raw bytes for the colour models that take them read
/// from the same stream, and a fresh 32-bit state every 131,072 symbols (<c>sync_code3</c>).
/// </summary>
internal sealed class Scpr3RangeDecoder {
  private readonly byte[] _data;
  private int _at;
  private uint _code;
  private uint _code1;

  internal Scpr3RangeDecoder(byte[] data, int at) {
    this._data = data;
    this._at = at;
    this._code = this._Le32();
  }

  internal int Left => this._data.Length - this._at;

  internal uint Slot => this._code & 0xFFF;

  private uint _Le32() {
    if (this._data.Length - this._at < 4) {
      this._at = this._data.Length;
      return 0;
    }

    var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(this._at));
    this._at += 4;
    return value;
  }

  private void _Sync() {
    if (++this._code1 == 0x20000) {
      this._code = this._Le32();
      this._code1 = 0;
    }
  }

  private void _Renormalise(uint code) {
    while (code < 0x800000 && this._at < this._data.Length)
      code = this._data[this._at++] | (code << 8);
    this._code = code;
  }

  internal void Consume(uint width, uint start) {
    unchecked {
      this._Renormalise(width * (this._code >> 12) + (this._code & 0xFFF) - start);
    }
    this._Sync();
  }

  internal int Literal() {
    var value = this._at < this._data.Length ? this._data[this._at++] : 0;
    this._Sync();
    return value;
  }

  /// <summary>The even binary choice an inter frame's moved block opens with: true keeps the last vector.</summary>
  internal bool Binary() {
    var a = this._code & 0xFFF;
    var keep = a >= 0x800;
    unchecked {
      this._Renormalise(a + ((this._code >> 1) & 0xFFFFF800) - (keep ? 2048u : 0u));
    }
    this._Sync();
    return keep;
  }
}

/// <summary>
/// The writing side of <see cref="Scpr3RangeDecoder"/>: symbols are collected in order, then each run
/// of 131,072 is rANS-coded backwards from the lowest state, so the reader finds them forwards.
/// </summary>
internal sealed class Scpr3SymbolWriter {
  private readonly List<(uint Width, uint Start, int Literal)> _events = [];

  internal void Symbol(uint width, uint start) => this._events.Add((width, start, -1));

  internal void Literal(byte value) => this._events.Add((0, 0, value));

  internal void Binary(bool keep) => this._events.Add((2048, keep ? 2048u : 0u, -1));

  internal void WriteTo(List<byte> output) {
    for (var chunk = 0; chunk == 0 || chunk < this._events.Count; chunk += 0x20000) {
      var end = Math.Min(chunk + 0x20000, this._events.Count);
      var stack = new List<byte>();
      uint x = 0x800000;
      for (var i = end - 1; i >= chunk; --i) {
        var (width, start, literal) = this._events[i];
        if (literal >= 0) {
          stack.Add((byte)literal);
          continue;
        }

        var limit = (ulong)width << 19;
        while (x >= limit) {
          stack.Add((byte)x);
          x >>= 8;
        }

        x = ((x / width) << 12) + x % width + start;
      }

      output.Add((byte)x);
      output.Add((byte)(x >> 8));
      output.Add((byte)(x >> 16));
      output.Add((byte)(x >> 24));
      for (var i = stack.Count - 1; i >= 0; --i)
        output.Add(stack[i]);
    }
  }
}
