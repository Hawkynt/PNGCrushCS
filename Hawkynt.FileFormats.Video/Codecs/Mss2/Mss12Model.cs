using System;

namespace FileFormat.Codecs.Mss2;

/// <summary>
/// An adaptive frequency model of MS Screen's arithmetic coder: FFmpeg's <c>Model</c> with
/// <c>model_reset</c>, <c>ff_mss12_model_update</c> and the weight rescaling, from
/// <c>libavcodec/mss12.c</c> (LGPL-2.1-or-later).
/// </summary>
/// <remarks>
/// The cumulative counts run downwards — entry nought is the total — and a symbol that becomes as
/// frequent as the one ranked above it swaps places with it, which keeps the table sorted by weight
/// without ever sorting it. The counts are sixteen-bit in FFmpeg and stay well inside that here,
/// because the rescaling halves them every time the total passes the threshold.
/// </remarks>
internal sealed class Mss12Model {

  /// <summary>The adaptive threshold, recomputed from the weights at every rescale.</summary>
  internal const int AdaptiveThreshold = -1;

  internal const int LowThreshold = 15;

  internal const int HighThreshold = 50;

  internal readonly int[] CumulativeProbability;
  private readonly int[] _weights;
  private readonly byte[] _indexToSymbol;
  private readonly int _symbolCount;
  private readonly int _thresholdWeight;
  private int _threshold;

  internal Mss12Model(int symbolCount, int thresholdWeight) {
    this._symbolCount = symbolCount;
    this._thresholdWeight = thresholdWeight;
    this._threshold = symbolCount * thresholdWeight;
    this.CumulativeProbability = new int[symbolCount + 1];
    this._weights = new int[symbolCount + 1];
    this._indexToSymbol = new byte[symbolCount + 1];
  }

  internal byte SymbolAt(int index) => this._indexToSymbol[index];

  /// <summary><c>model_reset</c>.</summary>
  internal void Reset() {
    for (var i = 0; i <= this._symbolCount; ++i) {
      this._weights[i] = 1;
      this.CumulativeProbability[i] = this._symbolCount - i;
    }

    this._weights[0] = 0;
    for (var i = 0; i < this._symbolCount; ++i)
      this._indexToSymbol[i + 1] = (byte)i;
  }

  /// <summary><c>ff_mss12_model_update</c>: the decoded entry gains a count.</summary>
  internal void Update(int value) {
    if (this._weights[value] == this._weights[value - 1]) {
      var i = value;
      while (this._weights[i - 1] == this._weights[value])
        --i;
      if (i != value) {
        (this._indexToSymbol[value], this._indexToSymbol[i]) = (this._indexToSymbol[i], this._indexToSymbol[value]);
        value = i;
      }
    }

    ++this._weights[value];
    for (var i = value - 1; i >= 0; --i)
      ++this.CumulativeProbability[i];
    this._Rescale();
  }

  private void _Rescale() {
    if (this._thresholdWeight == AdaptiveThreshold) {
      var threshold = 2 * this._weights[this._symbolCount] - 1;
      threshold = ((threshold >> 1) + 4 * this.CumulativeProbability[0]) / threshold;
      this._threshold = Math.Min(threshold, 0x3FFF);
    }

    while (this.CumulativeProbability[0] > this._threshold) {
      var cumulative = 0;
      for (var i = this._symbolCount; i >= 0; --i) {
        this.CumulativeProbability[i] = cumulative;
        this._weights[i] = (this._weights[i] + 1) >> 1;
        cumulative += this._weights[i];
      }
    }
  }
}

/// <summary>What MS Screen's region coder needs of an arithmetic decoder.</summary>
internal interface IMss12Coder {

  /// <summary>Decodes a symbol out of <paramref name="model"/> and updates the model.</summary>
  int ReadSymbol(Mss12Model model);

  /// <summary>Decodes a uniformly distributed number below <paramref name="n"/>.</summary>
  int ReadNumber(int n);
}

/// <summary>
/// MS Screen 2's arithmetic decoder, FFmpeg's <c>arith2</c>: 24 bits of state, renormalised a byte at a
/// time, with Stuiver and Moffat's piecewise integer mapping for the non-power-of-two ranges.
/// </summary>
internal sealed class Mss2ArithmeticCoder : IMss12Coder {

  private readonly byte[] _data;
  private int _position;
  private int _low;
  private int _high;
  private int _value;

  /// <summary>Starts at <paramref name="offset"/>; reads past the end come back as nought, as FFmpeg's padding does.</summary>
  internal Mss2ArithmeticCoder(byte[] data, int offset) {
    this._data = data;
    this._position = offset;
    this._low = 0;
    this._high = 0xFFFFFF;
    this._start = offset;
    this._value = (this._Byte() << 16) | (this._Byte() << 8) | this._Byte();
  }

  private readonly int _start;

  /// <summary>
  /// <c>bytestream2_get_byte</c> over the packet and the two bytes of zero padding FFmpeg lets the
  /// coder see past it: inside them a read advances, beyond them it stays put and reads nought.
  /// </summary>
  private int _Byte() {
    if (this._position >= this._data.Length + 2)
      return 0;
    var value = this._position < this._data.Length ? this._data[this._position] : 0;
    ++this._position;
    return value;
  }

  private void _Normalise() {
    while ((this._high >> 15) - (this._low >> 15) < 2) {
      if (((this._low ^ this._high) & 0x10000) != 0) {
        this._high ^= 0x8000;
        this._value ^= 0x8000;
        this._low ^= 0x8000;
      }

      this._high = ((this._high & 0xFFFF) << 8) | 0xFF;
      this._value = ((this._value & 0xFFFF) << 8) | this._Byte();
      this._low = (this._low & 0xFFFF) << 8;
    }
  }

  /// <summary><c>arith2_get_bit</c>.</summary>
  internal int ReadBit() {
    var range = this._high - this._low + 1;
    var bit = 2 * this._value - this._low >= this._high ? 1 : 0;
    if (bit != 0)
      this._low += range >> 1;
    else
      this._high = this._low + (range >> 1) - 1;
    this._Normalise();
    return bit;
  }

  private static int _Scaled(int value, int n, int range) {
    var split = (n << 1) - range;
    return value > split ? split + ((value - split) >> 1) : value;
  }

  private void _Rescale(int range, int low, int high, int n) {
    var split = (n << 1) - range;
    this._high = high > split ? split + ((high - split) << 1) : high;
    this._high += this._low - 1;
    this._low += low > split ? split + ((low - split) << 1) : low;
  }

  private static int _Log2(int v) => 31 - int.LeadingZeroCount(v);

  /// <summary><c>arith2_get_number</c>.</summary>
  public int ReadNumber(int n) {
    var range = this._high - this._low + 1;
    var scale = _Log2(range) - _Log2(n);
    if (n << scale > range)
      --scale;
    n <<= scale;
    var value = _Scaled(this._value - this._low, n, range) >> scale;
    this._Rescale(range, value << scale, (value + 1) << scale, n);
    this._Normalise();
    return value;
  }

  /// <summary><c>arith2_get_model_sym</c>.</summary>
  public int ReadSymbol(Mss12Model model) {
    var probabilities = model.CumulativeProbability;
    var range = this._high - this._low + 1;
    var n = probabilities[0];
    var scale = _Log2(range) - _Log2(n);
    if (n << scale > range)
      --scale;
    n <<= scale;
    var value = _Scaled(this._value - this._low, n, range) >> scale;
    var i = 0;
    while (probabilities[++i] > value) {
    }

    this._Rescale(range, probabilities[i] << scale, probabilities[i - 1] << scale, n);
    var symbol = model.SymbolAt(i);
    model.Update(i);
    this._Normalise();
    return symbol;
  }

  /// <summary><c>arith2_get_consumed_bytes</c>: how far into the data the coded part reached.</summary>
  internal int ConsumedBytes {
    get {
      var difference = (this._high >> 16) - (this._low >> 16);
      var bitPosition = (this._position - this._start - 3) << 3;
      var bits = 1;
      while ((difference & 0x80) == 0) {
        ++bits;
        difference <<= 1;
      }

      return ((bits + bitPosition + 7) >> 3) + ((this._low >> 16) + 1 == this._high >> 16 ? 1 : 0);
    }
  }
}
