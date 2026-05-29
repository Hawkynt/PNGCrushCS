namespace FileFormat.WebP.Vp8;

/// <summary>
/// VP8 bitstream partition — boolean arithmetic decoder per RFC 6386 chapter 7.
/// Faithful port of partition.go from golang.org/x/image/vp8, which follows libwebp's
/// look-up-table-based range-coding approach rather than the RFC's reference C code.
/// </summary>
internal sealed class Vp8Partition {

  // Renormalization look-up tables from partition.go: for rangeM1 values 0..126, the
  // number of shift bits needed to bring range back to [128, 254] and the new rangeM1.
  private static readonly byte[] _LutShift = [
    7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4,
    3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
  ];

  private static readonly byte[] _LutRangeM1 = [
    127,
    127, 191,
    127, 159, 191, 223,
    127, 143, 159, 175, 191, 207, 223, 239,
    127, 135, 143, 151, 159, 167, 175, 183, 191, 199, 207, 215, 223, 231, 239, 247,
    127, 131, 135, 139, 143, 147, 151, 155, 159, 163, 167, 171, 175, 179, 183, 187,
    191, 195, 199, 203, 207, 211, 215, 219, 223, 227, 231, 235, 239, 243, 247, 251,
    127, 129, 131, 133, 135, 137, 139, 141, 143, 145, 147, 149, 151, 153, 155, 157,
    159, 161, 163, 165, 167, 169, 171, 173, 175, 177, 179, 181, 183, 185, 187, 189,
    191, 193, 195, 197, 199, 201, 203, 205, 207, 209, 211, 213, 215, 217, 219, 221,
    223, 225, 227, 229, 231, 233, 235, 237, 239, 241, 243, 245, 247, 249, 251, 253,
  ];

  /// <summary>Probability representing a 50/50 bit (uniform coding).</summary>
  public const byte UniformProb = 128;

  private byte[] _buf = [];
  private int _r;
  private uint _rangeM1;
  private uint _bits;
  private byte _nBits;

  /// <summary>True if we tried to read past end-of-buffer (matches Go's unexpectedEOF).</summary>
  public bool UnexpectedEof { get; private set; }

  /// <summary>Initialize the partition with the entire byte slice to be arithmetic-decoded.</summary>
  public void Init(byte[] buf) {
    _buf = buf;
    _r = 0;
    _rangeM1 = 254;
    _bits = 0;
    _nBits = 0;
    UnexpectedEof = false;
  }

  /// <summary>Read one bit with 0-probability = prob/256.</summary>
  public bool ReadBit(byte prob) {
    if (_nBits < 8) {
      if (_r >= _buf.Length) {
        UnexpectedEof = true;
        return false;
      }
      uint x = _buf[_r];
      _bits |= x << (8 - _nBits);
      ++_r;
      _nBits += 8;
    }
    var split = (_rangeM1 * prob >> 8) + 1;
    var bit = _bits >= split << 8;
    if (bit) {
      _rangeM1 -= split;
      _bits -= split << 8;
    } else {
      _rangeM1 = split - 1;
    }
    if (_rangeM1 < 127) {
      var shift = _LutShift[_rangeM1];
      _rangeM1 = _LutRangeM1[_rangeM1];
      _bits <<= shift;
      _nBits -= shift;
    }
    return bit;
  }

  /// <summary>Read an n-bit unsigned integer MSB-first using the given fixed probability.</summary>
  public uint ReadUint(byte prob, int n) {
    uint u = 0;
    while (n > 0) {
      --n;
      if (ReadBit(prob))
        u |= 1u << n;
    }
    return u;
  }

  /// <summary>Read an n-bit signed integer: magnitude then sign bit.</summary>
  public int ReadInt(byte prob, int n) {
    var u = (int)ReadUint(prob, n);
    return ReadBit(prob) ? -u : u;
  }

  /// <summary>Read a signed value that is most likely zero (flag bit then optional int).</summary>
  public int ReadOptionalInt(byte prob, int n) => !ReadBit(prob) ? 0 : ReadInt(prob, n);
}
