using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace FileFormat.Codecs.Lagarith;

/// <summary>A most-significant-bit-first reader over a plane's probability header.</summary>
internal ref struct LagarithBitReader(ReadOnlySpan<byte> data) {
  private readonly ReadOnlySpan<byte> _data = data;
  private int _bit;

  internal readonly int BitPosition => this._bit;

  internal int ReadBit() {
    var at = this._bit >> 3;
    var value = at < this._data.Length ? (this._data[at] >> (7 - (this._bit & 7))) & 1 : 0;
    ++this._bit;
    return value;
  }

  internal uint ReadBits(int count) {
    uint value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | (uint)this.ReadBit();
    return value;
  }

  internal readonly uint PeekBits(int count) {
    var copy = this;
    return copy.ReadBits(count);
  }
}

/// <summary>
/// Lagarith's probability header and range coder: FFmpeg's <c>lag_read_prob_header</c>,
/// <c>lag_decode_prob</c>, the soft-float rescaling that matches the x86 floating point the reference
/// encoder used, and <c>lagarithrac.h</c>'s decoder; and the encoder that is their inverse.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/lagarith.c</c>, <c>lagarithrac.c</c> and <c>lagarithrac.h</c>,
/// LGPL-2.1-or-later. The variable-length code of the header is copied exactly.
/// </remarks>
internal static class LagarithEntropy {

  // FFmpeg's lag_bits, lag_codes and lag_symbols: a sparse code, some symbols with two codewords.
  private static ReadOnlySpan<byte> _Bits => [
    7, 7, 2, 7, 3, 4, 5, 6, 7, 7, 7, 7, 7, 6, 7, 4, 5, 7, 7, 7, 7,
    5, 6, 7, 7, 7, 7, 7, 7, 6, 7, 7, 7, 7, 7, 6, 7, 7, 7, 7, 7, 7, 7,
    7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
  ];

  private static ReadOnlySpan<byte> _Codes => [
    0x01, 0x02, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x04, 0x05,
    0x08, 0x09, 0x0A, 0x0B, 0x0B, 0x0B, 0x0B, 0x10, 0x11, 0x12, 0x13,
    0x13, 0x13, 0x14, 0x15, 0x20, 0x21, 0x22, 0x23, 0x23, 0x24, 0x25,
    0x28, 0x29, 0x2A, 0x2B, 0x2B, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45,
    0x48, 0x49, 0x4A, 0x4B, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55,
  ];

  private static ReadOnlySpan<byte> _Symbols => [
    20, 12, 0, 12, 1, 2, 4, 7, 7, 28, 4, 25, 17,
    10, 17, 3, 6, 2, 23, 15, 15, 5, 9, 10, 31, 1, 22,
    14, 14, 8, 9, 30, 6, 27, 19, 11, 19, 0, 21, 13, 13,
    8, 29, 5, 26, 18, 18, 3, 24, 16, 16, 11, 32,
  ];

  /// <summary>The shortest codeword for each bit count 0-31, for the encoder.</summary>
  private static readonly (int Code, int Length)[] _Shortest = _BuildShortest();

  private static (int, int)[] _BuildShortest() {
    var table = new (int Code, int Length)[33];
    for (var i = 0; i < table.Length; ++i)
      table[i] = (0, int.MaxValue);
    for (var i = 0; i < _Bits.Length; ++i)
      if (_Bits[i] < table[_Symbols[i]].Length)
        table[_Symbols[i]] = (_Codes[i], _Bits[i]);
    return table;
  }

  /// <summary>One probability: a code for its bit count, then that many bits below an implied leading one.</summary>
  private static uint _ReadProbability(ref LagarithBitReader reader) {
    var bits = -1;
    var code = 0;
    for (var length = 1; length <= 7 && bits < 0; ++length) {
      code = (code << 1) | reader.ReadBit();
      for (var i = 0; i < _Bits.Length; ++i)
        if (_Bits[i] == length && _Codes[i] == code) {
          bits = _Symbols[i];
          break;
        }
    }

    if (bits < 0 || bits > 31)
      throw new InvalidDataException("A Lagarith plane's probability header holds an invalid code.");
    if (bits == 0)
      return 0;

    var value = reader.ReadBits(bits) | (1u << bits);
    return value - 1;
  }

  private static int _Log2(ulong value) => value == 0 ? 0 : BitOperations.Log2(value);

  /// <summary>The 52-bit mantissa of 1/denominator, FFmpeg's <c>softfloat_reciprocal</c>.</summary>
  private static ulong _Reciprocal(uint denominator) {
    var shift = _Log2(denominator - 1) + 1;
    var result = (1UL << 52) / denominator;
    var error = (1UL << 52) - result * denominator;
    result <<= shift;
    error <<= shift;
    error += denominator / 2;
    return result + error / denominator;
  }

  /// <summary>(uint)(x * f) for the mantissa f, FFmpeg's <c>softfloat_mul</c>.</summary>
  private static uint _Multiply(uint x, ulong mantissa) {
    var low = x * (mantissa & 0xFFFFFFFF);
    var high = x * (mantissa >> 32);
    high += low >> 32;
    low &= 0xFFFFFFFF;
    low += 1UL << _Log2(high >> 21);
    high += low >> 32;
    return (uint)(high >> 20);
  }

  /// <summary>
  /// Reads a plane's 256 probabilities and turns them into the cumulative table the coder uses,
  /// rescaled to a power of two exactly as the reference decoder does.
  /// </summary>
  /// <param name="probabilities">Receives 258 cumulative entries.</param>
  /// <returns>The scale: the cumulative total is two to this power.</returns>
  internal static int ReadProbabilities(ref LagarithBitReader reader, Span<uint> probabilities) {
    probabilities[0] = 0;
    probabilities[257] = uint.MaxValue;
    ulong total = 0;
    var nonZero = 0;
    for (var i = 1; i < 257; ++i) {
      probabilities[i] = _ReadProbability(ref reader);
      if (total + probabilities[i] > uint.MaxValue)
        throw new InvalidDataException("A Lagarith plane's probabilities overflow their total.");
      total += probabilities[i];
      if (probabilities[i] == 0) {
        var run = _ReadProbability(ref reader);
        if (run > 256 - i)
          run = (uint)(256 - i);
        for (var j = 0; j < run; ++j)
          probabilities[++i] = 0;
      } else
        ++nonZero;
    }

    if (total == 0)
      throw new InvalidDataException("Every probability of a Lagarith plane is zero.");
    if (nonZero == 1 && (reader.PeekBits(32) & 0xFFFFFF) != 0)
      throw new InvalidDataException("A Lagarith plane with one symbol carries coded data, which it cannot.");

    var cumulative = (uint)total;
    var scale = _Log2(cumulative);
    if ((cumulative & (cumulative - 1)) != 0) {
      var mantissa = _Reciprocal(cumulative);
      uint scaled = 0;
      for (var i = 1; i < 257; ++i) {
        probabilities[i] = _Multiply(probabilities[i], mantissa);
        scaled += probabilities[i];
      }

      ++scale;
      if (scale >= 32)
        throw new InvalidDataException("A Lagarith plane's probabilities scale past 32 bits.");
      var target = 1u << scale;
      if (scaled > target)
        throw new InvalidDataException("A Lagarith plane's scaled probabilities exceed their target.");

      // The remainder goes one at a time to the non-zero entries among the first 128, round and round.
      var remainder = target - scaled;
      for (var i = 1; remainder != 0; i = (i & 0x7F) + 1)
        if (probabilities[i] != 0) {
          ++probabilities[i];
          --remainder;
        }
    }

    if (scale > 23)
      throw new InvalidDataException($"A Lagarith plane's probabilities need {scale} bits of precision; the coder has 23.");

    for (var i = 1; i < 257; ++i)
      probabilities[i] += probabilities[i - 1];
    return scale;
  }

  /// <summary>
  /// Writes probabilities for the given counts that, read back, give a table the counts can be coded
  /// with: the counts themselves, halved until their total fits the coder's 23 bits. Fills
  /// <paramref name="cumulative"/> with the table the decoder will build and answers its scale.
  /// </summary>
  internal static int WriteProbabilities(LagarithBitWriter writer, ReadOnlySpan<long> counts, Span<uint> cumulative) {
    Span<uint> values = stackalloc uint[256];
    long total;
    var shift = 0;
    while (true) {
      total = 0;
      for (var i = 0; i < 256; ++i) {
        values[i] = counts[i] == 0 ? 0 : (uint)Math.Max(1, counts[i] >> shift);
        total += values[i];
      }
      if (total <= 1 << 22)
        break;
      ++shift;
    }

    for (var i = 0; i < 256;) {
      _WriteProbability(writer, values[i]);
      if (values[i] != 0) {
        ++i;
        continue;
      }

      var run = 0;
      while (i + 1 + run < 256 && values[i + 1 + run] == 0)
        ++run;
      _WriteProbability(writer, (uint)run);
      i += 1 + run;
    }

    return _Table(cumulative, values);
  }

  /// <summary>The cumulative table the decoder builds from the values just written.</summary>
  private static int _Table(Span<uint> probabilities, ReadOnlySpan<uint> values) {
    probabilities[0] = 0;
    probabilities[257] = uint.MaxValue;
    ulong total = 0;
    for (var i = 0; i < 256; ++i) {
      probabilities[i + 1] = values[i];
      total += values[i];
    }

    var cumulative = (uint)total;
    var scale = _Log2(cumulative);
    if ((cumulative & (cumulative - 1)) != 0) {
      var mantissa = _Reciprocal(cumulative);
      uint scaled = 0;
      for (var i = 1; i < 257; ++i) {
        probabilities[i] = _Multiply(probabilities[i], mantissa);
        scaled += probabilities[i];
      }
      ++scale;
      var remainder = (1u << scale) - scaled;
      for (var i = 1; remainder != 0; i = (i & 0x7F) + 1)
        if (probabilities[i] != 0) {
          ++probabilities[i];
          --remainder;
        }
    }

    for (var i = 1; i < 257; ++i)
      probabilities[i] += probabilities[i - 1];
    return scale;
  }

  private static void _WriteProbability(LagarithBitWriter writer, uint value) {
    if (value == 0) {
      var (zeroCode, zeroLength) = _Shortest[0];
      writer.Write((uint)zeroCode, zeroLength);
      return;
    }

    var bits = BitOperations.Log2(value + 1);
    var (code, length) = _Shortest[bits];
    writer.Write((uint)code, length);
    writer.Write((value + 1) & ((1u << bits) - 1), bits);
  }
}

/// <summary>A most-significant-bit-first writer for the probability header.</summary>
internal sealed class LagarithBitWriter {
  private readonly List<byte> _bytes = [];
  private int _current;
  private int _filled;

  internal int BitCount => this._bytes.Count * 8 + this._filled;

  internal void Write(uint value, int count) {
    for (var i = count - 1; i >= 0; --i) {
      this._current = (this._current << 1) | (int)((value >> i) & 1);
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
    return result.ToArray();
  }
}

/// <summary>Lagarith's range decoder over one plane's coded bytes, FFmpeg's <c>lag_get_rac</c>.</summary>
internal sealed class LagarithRangeDecoder {

  private const int _MAX_OVERREAD = 4;

  private readonly byte[] _data;
  private readonly int _end;
  private readonly uint[] _probabilities;
  private readonly int _scale;
  private int _position;
  private uint _low;
  private uint _range;

  internal int Overread { get; private set; }

  internal LagarithRangeDecoder(byte[] data, int start, int end, uint[] probabilities, int scale) {
    this._data = data;
    this._position = start;
    this._end = end;
    this._probabilities = probabilities;
    this._scale = scale;
    this._range = 0x80;
    this._low = (uint)(this._Byte(start) >> 1);
  }

  private int _Byte(int at) => at < this._end ? this._data[at] : 0;

  internal byte Read() {
    while (this._range <= 0x800000) {
      this._low <<= 8;
      this._range <<= 8;
      this._low |= (uint)(((this._Byte(this._position) << 8) | this._Byte(this._position + 1)) >> 1) & 0xFF;
      if (this._position < this._end)
        ++this._position;
      else if (++this.Overread > _MAX_OVERREAD * 64)
        throw new InvalidDataException("A Lagarith plane's coded data ran out long before the plane did.");
    }

    var probabilities = this._probabilities;
    var scaled = this._range >> this._scale;
    int value;
    if (this._low < scaled * probabilities[255]) {
      // The first symbol whose cumulative probability lies above the code value.
      int low = 0, high = 255;
      while (low < high) {
        var middle = (low + high) >> 1;
        if (this._low < scaled * probabilities[middle + 1])
          high = middle;
        else
          low = middle + 1;
      }
      value = low;
      this._range = scaled * (probabilities[value + 1] - probabilities[value]);
    } else {
      value = 255;
      this._range -= scaled * probabilities[255];
    }

    if (this._range == 0)
      this._range = 0x80;
    this._low -= scaled * probabilities[value];
    return (byte)value;
  }
}

/// <summary>
/// The encoder Lagarith's range decoder reads: the same arithmetic on the bottom of the interval,
/// carried out at twice the decoder's scale so that the one-bit offset at which the decoder reads its
/// bytes becomes a whole byte.
/// </summary>
internal sealed class LagarithRangeEncoder(uint[] probabilities, int scale) {

  private readonly List<byte> _bytes = [];
  private ulong _low;
  private int _window = 1;
  private uint _range = 0x80;

  internal void Write(byte value) {
    while (this._range <= 0x800000) {
      this._range <<= 8;
      this._low <<= 8;
      if (++this._window > 6) {
        this._bytes.Add((byte)(this._low >> 48));
        this._low &= (1UL << 48) - 1;
        --this._window;
      }
    }

    var scaled = this._range >> scale;
    ulong bottom;
    if (value == 255) {
      bottom = (ulong)scaled * probabilities[255];
      this._range -= scaled * probabilities[255];
    } else {
      bottom = (ulong)scaled * probabilities[value];
      this._range = scaled * (probabilities[value + 1] - probabilities[value]);
    }

    // Twice the decoder's bottom, because a byte here is the decoder's bit offset by one.
    this._low += bottom << 1;
    var limit = 1UL << (8 * this._window);
    if (this._low >= limit) {
      this._low -= limit;
      for (var i = this._bytes.Count - 1; i >= 0 && ++this._bytes[i] == 0; --i) { }
    }
  }

  internal byte[] Finish() {
    var result = new List<byte>(this._bytes);
    for (var i = this._window - 1; i >= 0; --i)
      result.Add((byte)(this._low >> (8 * i)));
    result.Add(0);
    result.Add(0);
    return result.ToArray();
  }
}
