namespace FileFormat.WebP.Vp8;

/// <summary>VP8 boolean arithmetic (range) decoder - the core entropy coding mechanism.</summary>
internal sealed class Vp8BoolDecoder {
  private uint _range;
  private uint _value;
  private int _bitsLeft;
  private readonly byte[] _data;
  private int _pos;

  public Vp8BoolDecoder(byte[] data, int offset) {
    _data = data;
    _pos = offset;
    _range = 255;
    _value = 0;
    for (var i = 0; i < 2 && offset + i < data.Length; ++i)
      _value = (_value << 8) | data[offset + i];
    _pos = offset + 2;
    _bitsLeft = 16;
  }

  /// <summary>Read one boolean with probability prob/256 of being 0.</summary>
  public int ReadBool(int prob) {
    var split = 1 + ((_range - 1) * (uint)prob >> 8);
    var bit = _value >= split ? 1 : 0;
    if (bit != 0) {
      _value -= split;
      _range -= split;
    } else {
      _range = split;
    }

    while (_range < 128) {
      _range <<= 1;
      _value <<= 1;
      if (--_bitsLeft == 0) {
        _bitsLeft = 8;
        if (_pos < _data.Length)
          _value |= _data[_pos++];
      }
    }

    return bit;
  }

  /// <summary>Read a literal value of n bits (MSB first), each with prob=128.</summary>
  public int ReadLiteral(int n) {
    var v = 0;
    for (var i = 0; i < n; ++i)
      v = (v << 1) | ReadBool(128);
    return v;
  }

  /// <summary>Read a signed literal: magnitude then optional sign bit.</summary>
  public int ReadSignedLiteral(int n) {
    var v = ReadLiteral(n);
    return ReadBool(128) != 0 ? -v : v;
  }
}
