using System;
using System.IO;

namespace FileFormat.Ecw.Codec;

/// <summary>
/// Byte-aligned range encoder for ECW coefficient coding.
/// Uses Dmitry Subbotin's carryless range coder implementation:
/// 32-bit range with bottom=2^16, top=2^24 thresholds.
/// </summary>
internal sealed class EcwRangeEncoder : IDisposable {

  private const uint _TOP = 1u << 24;
  private const uint _BOT = 1u << 16;

  private readonly MemoryStream _output;
  private uint _low;
  private uint _range;

  public EcwRangeEncoder(int initialCapacity = 4096) {
    _output = new(initialCapacity);
    _low = 0;
    _range = uint.MaxValue;
  }

  /// <summary>Encodes a binary decision using the given adaptive context.</summary>
  public void EncodeBit(int bit, EcwContext ctx) {
    var bound = (uint)(((ulong)_range * (uint)ctx.Probability) >> 12);
    if (bound < 1) bound = 1;
    if (bound >= _range) bound = _range - 1;

    if (bit == 0) {
      _range = bound;
    } else {
      _low += bound;
      _range -= bound;
    }

    ctx.Update(bit);

    while (_range < _BOT) {
      if ((_low ^ (_low + _range)) < _TOP) {
        // Top byte is settled -- output it
      } else {
        // Top byte not settled and range too small: shrink range to force it
        _range = ((uint)(-(int)_low)) & (_BOT - 1);
        if (_range == 0) _range = _BOT;
      }
      _output.WriteByte((byte)(_low >> 24));
      _low <<= 8;
      _range <<= 8;
    }
  }

  /// <summary>Encodes an integer coefficient using the sign-magnitude scheme with adaptive contexts.</summary>
  public void EncodeCoefficient(int value, EcwContextSet contexts) {
    if (value == 0) {
      EncodeBit(0, contexts.Significance);
      return;
    }

    EncodeBit(1, contexts.Significance);
    EncodeBit(value < 0 ? 1 : 0, contexts.Sign);

    var magnitude = Math.Abs(value);
    var bits = _BitLength(magnitude);

    for (var i = 0; i < bits - 1; ++i)
      EncodeBit(1, contexts.MagnitudePrefix);
    EncodeBit(0, contexts.MagnitudePrefix);

    for (var i = bits - 2; i >= 0; --i)
      EncodeBit((magnitude >> i) & 1, contexts.MagnitudeSuffix);
  }

  /// <summary>Flushes the encoder and returns the complete encoded byte stream.</summary>
  public byte[] Finish() {
    // Output 4 more bytes to flush the final state
    for (var i = 0; i < 4; ++i) {
      _output.WriteByte((byte)(_low >> 24));
      _low <<= 8;
    }
    return _output.ToArray();
  }

  public void Dispose() => _output.Dispose();

  private static int _BitLength(int value) {
    var bits = 0;
    while (value > 0) {
      value >>= 1;
      ++bits;
    }
    return Math.Max(bits, 1);
  }
}

/// <summary>
/// Byte-aligned range decoder for ECW coefficient decoding.
/// Exact mirror of <see cref="EcwRangeEncoder"/> using Subbotin's carryless scheme.
/// </summary>
internal sealed class EcwRangeDecoder {

  private const uint _TOP = 1u << 24;
  private const uint _BOT = 1u << 16;

  private readonly byte[] _data;
  private int _pos;
  private uint _low;
  private uint _range;
  private uint _code;

  public EcwRangeDecoder(byte[] data, int startOffset) {
    _data = data;
    _pos = startOffset;
    _low = 0;
    _range = uint.MaxValue;
    _code = 0;

    for (var i = 0; i < 4; ++i)
      _code = (_code << 8) | _Next();
  }

  /// <summary>Decodes a binary decision using the given adaptive context.</summary>
  public int DecodeBit(EcwContext ctx) {
    var bound = (uint)(((ulong)_range * (uint)ctx.Probability) >> 12);
    if (bound < 1) bound = 1;
    if (bound >= _range) bound = _range - 1;

    int bit;
    if (_code - _low < bound) {
      bit = 0;
      _range = bound;
    } else {
      bit = 1;
      _low += bound;
      _range -= bound;
    }

    ctx.Update(bit);

    while (_range < _BOT) {
      if ((_low ^ (_low + _range)) < _TOP) {
        // Top byte is settled
      } else {
        _range = ((uint)(-(int)_low)) & (_BOT - 1);
        if (_range == 0) _range = _BOT;
      }
      _code = (_code << 8) | _Next();
      _low <<= 8;
      _range <<= 8;
    }

    return bit;
  }

  /// <summary>Decodes an integer coefficient using the sign-magnitude scheme with adaptive contexts.</summary>
  public int DecodeCoefficient(EcwContextSet contexts) {
    if (DecodeBit(contexts.Significance) == 0)
      return 0;

    var sign = DecodeBit(contexts.Sign);

    var bits = 1;
    while (DecodeBit(contexts.MagnitudePrefix) != 0 && bits < 24)
      ++bits;

    var magnitude = 1;
    for (var i = 0; i < bits - 1; ++i)
      magnitude = (magnitude << 1) | DecodeBit(contexts.MagnitudeSuffix);

    return sign != 0 ? -magnitude : magnitude;
  }

  private byte _Next() => _pos < _data.Length ? _data[_pos++] : (byte)0;
}

/// <summary>
/// Adaptive binary probability context for the range coder.
/// 12-bit fixed-point probability (1..4095) with exponential moving average.
/// Probability represents the likelihood of symbol 0.
/// </summary>
internal sealed class EcwContext {

  private int _prob0 = 2048;
  private const int _RATE = 5;

  public int Probability => _prob0;

  public void Update(int bit) {
    if (bit == 0)
      _prob0 += (4096 - _prob0) >> _RATE;
    else
      _prob0 -= _prob0 >> _RATE;

    if (_prob0 < 1) _prob0 = 1;
    else if (_prob0 > 4095) _prob0 = 4095;
  }

  public void Reset() => _prob0 = 2048;
}

/// <summary>
/// Context set for encoding/decoding one coefficient.
/// Separates significance, sign, magnitude prefix, and magnitude suffix contexts.
/// </summary>
internal sealed class EcwContextSet {

  public readonly EcwContext Significance = new();
  public readonly EcwContext Sign = new();
  public readonly EcwContext MagnitudePrefix = new();
  public readonly EcwContext MagnitudeSuffix = new();

  public void Reset() {
    Significance.Reset();
    Sign.Reset();
    MagnitudePrefix.Reset();
    MagnitudeSuffix.Reset();
  }
}
