using System;
using System.IO;

namespace FileFormat.Fif;

/// <summary>The 24-bit range decoder of FTC codec profile 2, with its adaptive-free model descriptions.</summary>
internal sealed class FifArithmeticDecoder {

  private const int _TOTAL = 0x4000;

  private readonly byte[] _data;
  private int _interval;
  private int _code;

  /// <summary>The next byte the decoder (or the raw side channel) reads.</summary>
  public int Position { get; set; }

  /// <summary>The next bit of the raw side channel within <see cref="Position"/>.</summary>
  public int BitPosition { get; set; }

  public FifArithmeticDecoder(byte[] data) {
    _data = data;
    Restart();
  }

  private int _Byte() {
    if (Position >= _data.Length)
      throw new InvalidDataException("Truncated FTC arithmetic stream.");

    return _data[Position++];
  }

  public void Restart() {
    _code = (_Byte() << 16) | (_Byte() << 8) | _Byte();
    _interval = 0x1000000;
  }

  /// <summary>One bit of the side channel that shares the arithmetic coder's byte cursor.</summary>
  public int RawBit() {
    if (Position >= _data.Length)
      throw new InvalidDataException("Truncated FTC arithmetic side channel.");

    var value = (_data[Position] >> BitPosition) & 1;
    if (++BitPosition == 8) {
      BitPosition = 0;
      ++Position;
    }

    return value;
  }

  /// <summary>Decodes one symbol under a reverse cumulative model from 0x4000 down to 0.</summary>
  public int Decode(int[] cumulative) {
    var symbol = -1;
    long lower = 0, upper = 0;
    for (var i = 0; i < cumulative.Length - 1; ++i) {
      upper = (long)_interval * cumulative[i] / _TOTAL;
      lower = (long)_interval * cumulative[i + 1] / _TOTAL;
      if (lower <= _code && _code < upper) {
        symbol = i;
        break;
      }
    }

    if (symbol < 0 || upper <= lower)
      throw new InvalidDataException("FTC arithmetic code selects a zero-probability symbol.");

    _code -= (int)lower;
    _interval = (int)(upper - lower);
    while (_interval < 0x8000) {
      _interval <<= 8;
      _code = (_code << 8) | _Byte();
    }

    return symbol;
  }

  public int Uniform(int count) {
    var cumulative = new int[count + 1];
    var step = _TOTAL / count;
    for (var i = 0; i <= count; ++i)
      cumulative[i] = _TOTAL - i * step;

    return Decode(cumulative);
  }

  /// <summary>Reads the description of one alphabet's model and returns its cumulative table.</summary>
  public int[] Model(int count) {
    if (count is <= 0 or > 0x10000)
      throw new InvalidDataException($"Invalid FTC arithmetic alphabet size {count}.");

    var weights = new long[count];
    if (count < 32)
      for (var i = 0; i < count; ++i) {
        var value = Uniform(16);
        weights[i] = value * value;
      }
    else {
      // A large alphabet sends a four-action predictor instead of every weight.
      var seed = new int[4];
      for (var i = 0; i < 4; ++i)
        seed[i] = Uniform(16);

      var total = seed[0] + seed[1] + seed[2] + seed[3];
      if (total == 0)
        throw new InvalidDataException("Zero-total FTC arithmetic seed model.");

      int[] control = [_TOTAL, (total - seed[0]) * _TOTAL / total, (seed[2] + seed[3]) * _TOTAL / total, seed[3] * _TOTAL / total, 0];
      long previous = 0;
      long delta = 1;
      for (var i = 0; i < count; ++i) {
        var action = Decode(control);
        long value;
        if (previous == 0) {
          delta = action switch { 0 => 1, 1 => 1, 2 => 4, _ => 12 };
          value = action switch { 0 => 0, 1 => 1, 2 => 4, _ => 12 };
        } else {
          switch (action) {
            case 1:
              delta = delta < 0 ? (1 - delta) / 2 : (-1 - delta) / 2;
              break;
            case 2:
              delta = delta < 0 ? (delta - 1) / 2 : (delta + 1) / 2;
              break;
            case 3:
              delta *= 2;
              break;
          }

          value = Math.Max(previous + delta, 0);
        }

        weights[i] = value;
        previous = value;
      }
    }

    long sum = 0;
    foreach (var weight in weights)
      sum += weight;
    if (sum == 0)
      throw new InvalidDataException("Zero-total FTC arithmetic model.");

    var result = new int[count + 1];
    long running = 0;
    for (var i = count - 1; i >= 0; --i) {
      running += weights[i];
      result[i] = (int)(running * _TOTAL / sum);
    }

    result[count] = 0;
    result[0] = _TOTAL;
    return result;
  }
}
