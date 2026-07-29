using System;

namespace FileFormat.Jbig2.Codec;

/// <summary>QM-coder arithmetic decoder as defined in ITU-T T.88 Annex E.
/// Uses the probability estimation table with 113 states, MPS/LPS tracking,
/// and renormalization via byte-stuffing after 0xFF markers.</summary>
internal sealed class Jbig2ArithmeticDecoder {

  /// <summary>One row of the QM probability estimation table (Table E.1 in T.88).</summary>
  private readonly record struct QeEntry(int Qe, int Nmps, int Nlps, bool SwitchFlag);

  // ITU-T T.88 Table E.1 - 113-state probability estimation table
  private static readonly QeEntry[] _QeTable = [
    new(0x5601,  1,  1, true),   //  0
    new(0x3401,  2,  6, false),  //  1
    new(0x1801,  3,  9, false),  //  2
    new(0x0AC1,  4, 12, false),  //  3
    new(0x0521,  5, 29, false),  //  4
    new(0x0221, 38, 33, false),  //  5
    new(0x5601,  7,  6, true),   //  6
    new(0x5401,  8, 14, false),  //  7
    new(0x4801,  9, 14, false),  //  8
    new(0x3801, 10, 14, false),  //  9
    new(0x3001, 11, 17, false),  // 10
    new(0x2401, 12, 18, false),  // 11
    new(0x1C01, 13, 20, false),  // 12
    new(0x1601, 29, 21, false),  // 13
    new(0x5601, 15, 14, true),   // 14
    new(0x5401, 16, 14, false),  // 15
    new(0x5101, 17, 15, false),  // 16
    new(0x4801, 18, 16, false),  // 17
    new(0x3801, 19, 17, false),  // 18
    new(0x3401, 20, 18, false),  // 19
    new(0x3001, 21, 19, false),  // 20
    new(0x2801, 22, 19, false),  // 21
    new(0x2401, 23, 20, false),  // 22
    new(0x2201, 24, 21, false),  // 23
    new(0x1C01, 25, 22, false),  // 24
    new(0x1801, 26, 23, false),  // 25
    new(0x1601, 27, 24, false),  // 26
    new(0x1401, 28, 25, false),  // 27
    new(0x1201, 29, 26, false),  // 28
    new(0x1101, 30, 27, false),  // 29
    new(0x0AC1, 31, 28, false),  // 30
    new(0x09C1, 32, 29, false),  // 31
    new(0x08A1, 33, 30, false),  // 32
    new(0x0521, 34, 31, false),  // 33
    new(0x0441, 35, 32, false),  // 34
    new(0x02A1, 36, 33, false),  // 35
    new(0x0221, 37, 34, false),  // 36
    new(0x0141, 38, 35, false),  // 37
    new(0x0111, 39, 36, false),  // 38
    new(0x0085, 40, 37, false),  // 39
    new(0x0049, 41, 38, false),  // 40
    new(0x0025, 42, 39, false),  // 41
    new(0x0015, 43, 40, false),  // 42
    new(0x0009, 44, 41, false),  // 43
    new(0x0005, 45, 42, false),  // 44
    new(0x0001, 45, 43, false),  // 45
    new(0x5601, 46, 46, false),  // 46  (uniform context)
    // States 47-112: extended precision states
    new(0x5401, 48, 48, false),  // 47
    new(0x5101, 49, 49, false),  // 48
    new(0x4801, 50, 50, false),  // 49
    new(0x3801, 51, 51, false),  // 50
    new(0x3401, 52, 52, false),  // 51
    new(0x3001, 53, 53, false),  // 52
    new(0x2801, 54, 54, false),  // 53
    new(0x2401, 55, 55, false),  // 54
    new(0x2201, 56, 56, false),  // 55
    new(0x1C01, 57, 57, false),  // 56
    new(0x1801, 58, 58, false),  // 57
    new(0x1601, 59, 59, false),  // 58
    new(0x1401, 60, 60, false),  // 59
    new(0x1201, 61, 61, false),  // 60
    new(0x1101, 62, 62, false),  // 61
    new(0x0AC1, 63, 63, false),  // 62
    new(0x09C1, 64, 64, false),  // 63
    new(0x08A1, 65, 65, false),  // 64
    new(0x0521, 66, 66, false),  // 65
    new(0x0441, 67, 67, false),  // 66
    new(0x02A1, 68, 68, false),  // 67
    new(0x0221, 69, 69, false),  // 68
    new(0x0141, 70, 70, false),  // 69
    new(0x0111, 71, 71, false),  // 70
    new(0x0085, 72, 72, false),  // 71
    new(0x0049, 73, 73, false),  // 72
    new(0x0025, 74, 74, false),  // 73
    new(0x0015, 75, 75, false),  // 74
    new(0x0009, 76, 76, false),  // 75
    new(0x0005, 77, 77, false),  // 76
    new(0x0001, 78, 78, false),  // 77
    new(0x5601, 79, 79, false),  // 78
    new(0x5401, 80, 80, false),  // 79
    new(0x5101, 81, 81, false),  // 80
    new(0x4801, 82, 82, false),  // 81
    new(0x3801, 83, 83, false),  // 82
    new(0x3401, 84, 84, false),  // 83
    new(0x3001, 85, 85, false),  // 84
    new(0x2801, 86, 86, false),  // 85
    new(0x2401, 87, 87, false),  // 86
    new(0x2201, 88, 88, false),  // 87
    new(0x1C01, 89, 89, false),  // 88
    new(0x1801, 90, 90, false),  // 89
    new(0x1601, 91, 91, false),  // 90
    new(0x1401, 92, 92, false),  // 91
    new(0x1201, 93, 93, false),  // 92
    new(0x1101, 94, 94, false),  // 93
    new(0x0AC1, 95, 95, false),  // 94
    new(0x09C1, 96, 96, false),  // 95
    new(0x08A1, 97, 97, false),  // 96
    new(0x0521, 98, 98, false),  // 97
    new(0x0441, 99, 99, false),  // 98
    new(0x02A1, 100, 100, false), // 99
    new(0x0221, 101, 101, false), // 100
    new(0x0141, 102, 102, false), // 101
    new(0x0111, 103, 103, false), // 102
    new(0x0085, 104, 104, false), // 103
    new(0x0049, 105, 105, false), // 104
    new(0x0025, 106, 106, false), // 105
    new(0x0015, 107, 107, false), // 106
    new(0x0009, 108, 108, false), // 107
    new(0x0005, 109, 109, false), // 108
    new(0x0001, 110, 110, false), // 109
    new(0x5601, 111, 111, false), // 110
    new(0x5401, 112, 112, false), // 111
    new(0x5101, 112, 112, false), // 112
  ];

  private readonly byte[] _data;
  private int _offset;

  // Arithmetic decoder registers
  private uint _c;   // C register (code register)
  private uint _a;   // A register (interval)
  private int _ct;   // Counter (bits remaining before next byte read)
  private byte _b;   // Current byte buffer

  internal Jbig2ArithmeticDecoder(byte[] data, int offset) {
    _data = data;
    _offset = offset;
    _InitDec();
  }

  /// <summary>Current read position in the data buffer.</summary>
  internal int ByteOffset => _offset;

  /// <summary>Initializes the decoder state per T.88 E.3.1 (INITDEC).</summary>
  private void _InitDec() {
    _b = _ReadByte();

    _c = (uint)(_b ^ 0xFF) << 16;
    _ReadByteAdvance();
    _c |= (uint)(_b ^ 0xFF) << 8;
    _ReadByteAdvance();

    if (_b == 0xFF)
      _ct = 8;
    else
      _ct = 7;

    _a = 0x8000;
  }

  /// <summary>Decodes a single decision using the given context (T.88 procedure DECODE).</summary>
  internal int DecodeBit(Jbig2ContextModel.Context cx) {
    ref var entry = ref _QeTable[cx.I];
    var qe = (uint)entry.Qe;

    _a -= qe;

    int d;
    if ((_c >> 16) < _a) {
      // MPS sub-interval
      if ((_a & 0x8000) != 0)
        return cx.Mps;

      d = _MpsExchange(cx, qe);
    } else {
      // LPS sub-interval
      _c -= (uint)_a << 16;
      d = _LpsExchange(cx, qe);
    }

    _Renormalize();
    return d;
  }

  /// <summary>Decodes a single bit using IAID procedure (integer arithmetic integer decoder) with
  /// a direct context index rather than a context model structure. Uses separate arrays.</summary>
  internal int DecodeBit(int[] iIndex, int[] iMps, int cx) {
    ref var entry = ref _QeTable[iIndex[cx]];
    var qe = (uint)entry.Qe;

    _a -= qe;

    int d;
    if ((_c >> 16) < _a) {
      if ((_a & 0x8000) != 0)
        return iMps[cx];

      // MPS exchange inline
      if (_a < qe) {
        d = 1 - iMps[cx];
        if (entry.SwitchFlag)
          iMps[cx] = 1 - iMps[cx];
        iIndex[cx] = entry.Nlps;
      } else {
        d = iMps[cx];
        iIndex[cx] = entry.Nmps;
      }
    } else {
      _c -= (uint)_a << 16;
      // LPS exchange inline
      if (_a < qe) {
        d = iMps[cx];
        iIndex[cx] = entry.Nmps;
      } else {
        d = 1 - iMps[cx];
        if (entry.SwitchFlag)
          iMps[cx] = 1 - iMps[cx];
        iIndex[cx] = entry.Nlps;
      }
      _a = qe;
    }

    _Renormalize();
    return d;
  }

  private int _MpsExchange(Jbig2ContextModel.Context cx, uint qe) {
    ref var entry = ref _QeTable[cx.I];
    if (_a < qe) {
      var d = 1 - cx.Mps;
      if (entry.SwitchFlag)
        cx.Mps = 1 - cx.Mps;
      cx.I = entry.Nlps;
      _a = qe;
      return d;
    }

    cx.I = entry.Nmps;
    return cx.Mps;
  }

  private int _LpsExchange(Jbig2ContextModel.Context cx, uint qe) {
    ref var entry = ref _QeTable[cx.I];
    if (_a < qe) {
      cx.I = entry.Nmps;
      _a = qe;
      return cx.Mps;
    }

    var d = 1 - cx.Mps;
    if (entry.SwitchFlag)
      cx.Mps = 1 - cx.Mps;
    cx.I = entry.Nlps;
    _a = qe;
    return d;
  }

  /// <summary>Renormalization procedure (T.88 E.3.3 RENORMD).</summary>
  private void _Renormalize() {
    while ((_a & 0x8000) == 0) {
      if (_ct == 0)
        _ByteIn();

      _a <<= 1;
      _c <<= 1;
      --_ct;
    }
  }

  /// <summary>Byte input procedure with 0xFF byte-stuffing (T.88 E.3.4 BYTEIN).</summary>
  private void _ByteIn() {
    if (_b == 0xFF) {
      var b1 = _PeekByte();
      if (b1 > 0x8F) {
        _ct = 8;
      } else {
        _ReadByteAdvance();
        _c += 0xFE00 - ((uint)_b << 9);
        _ct = 7;
      }
    } else {
      _ReadByteAdvance();
      _c += 0xFF00 - ((uint)_b << 8);
      _ct = 8;
    }
  }

  private byte _ReadByte()
    => _offset < _data.Length ? _data[_offset] : (byte)0xFF;

  private byte _PeekByte()
    => _offset < _data.Length ? _data[_offset] : (byte)0xFF;

  private void _ReadByteAdvance() {
    if (_offset < _data.Length)
      _b = _data[_offset++];
    else
      _b = 0xFF;
  }

  /// <summary>Decodes a JBIG2 integer value using the procedure described in T.88 section 6.4.6.</summary>
  internal int? DecodeInteger(Jbig2ContextModel contexts) {
    var prev = 1;

    // Decode S (sign)
    var s = DecodeBit(contexts[prev]);
    prev = (prev << 1) | s;

    // Decode value using variable-length prefix coding
    int v;
    var bit = DecodeBit(contexts[prev]);
    prev = (prev << 1) | bit;

    if (bit == 0) {
      // V is in range [0,3]
      v = _DecodeIntBits(contexts, ref prev, 2);
    } else {
      bit = DecodeBit(contexts[prev]);
      prev = (prev << 1) | bit;

      if (bit == 0) {
        // V is in range [4,19]
        v = _DecodeIntBits(contexts, ref prev, 4) + 4;
      } else {
        bit = DecodeBit(contexts[prev]);
        prev = (prev << 1) | bit;

        if (bit == 0) {
          // V is in range [20,83]
          v = _DecodeIntBits(contexts, ref prev, 6) + 20;
        } else {
          bit = DecodeBit(contexts[prev]);
          prev = (prev << 1) | bit;

          if (bit == 0) {
            // V is in range [84,339]
            v = _DecodeIntBits(contexts, ref prev, 8) + 84;
          } else {
            bit = DecodeBit(contexts[prev]);
            prev = (prev << 1) | bit;

            if (bit == 0) {
              // V is in range [340,4435]
              v = _DecodeIntBits(contexts, ref prev, 12) + 340;
            } else {
              // V is in range [4436,...]
              v = _DecodeIntBits(contexts, ref prev, 32) + 4436;
            }
          }
        }
      }
    }

    return s == 0 ? v : v > 0 ? -v : (int?)null;
  }

  private int _DecodeIntBits(Jbig2ContextModel contexts, ref int prev, int count) {
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var bit = DecodeBit(contexts[prev]);
      prev = ((prev << 1) | bit) & 0x1FF; // limit context growth
      value = (value << 1) | bit;
    }
    return value;
  }

  /// <summary>Decodes an IAID value (T.88 6.4.10 - Arithmetic Integer Arithmetic ID decoder).
  /// Uses a separate set of contexts indexed directly by accumulated bits.</summary>
  internal int DecodeIaid(int symCodeLen, int[] iaidCtxI, int[] iaidCtxMps) {
    var prev = 1;
    for (var i = 0; i < symCodeLen; ++i) {
      var d = DecodeBit(iaidCtxI, iaidCtxMps, prev);
      prev = (prev << 1) | d;
    }
    return prev - (1 << symCodeLen);
  }
}
