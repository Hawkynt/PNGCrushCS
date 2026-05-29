using System;
using System.IO;

namespace FileFormat.Jbig2.Codec;

/// <summary>QM-coder arithmetic encoder as defined in ITU-T T.88 Annex E.
/// Mirrors the decoder's 113-state probability estimation table.</summary>
internal sealed class Jbig2ArithmeticEncoder {

  /// <summary>One row of the QM probability estimation table.</summary>
  private readonly record struct QeEntry(int Qe, int Nmps, int Nlps, bool SwitchFlag);

  // Same table as decoder (T.88 Table E.1)
  private static readonly QeEntry[] _QeTable = [
    new(0x5601,  1,  1, true),
    new(0x3401,  2,  6, false),
    new(0x1801,  3,  9, false),
    new(0x0AC1,  4, 12, false),
    new(0x0521,  5, 29, false),
    new(0x0221, 38, 33, false),
    new(0x5601,  7,  6, true),
    new(0x5401,  8, 14, false),
    new(0x4801,  9, 14, false),
    new(0x3801, 10, 14, false),
    new(0x3001, 11, 17, false),
    new(0x2401, 12, 18, false),
    new(0x1C01, 13, 20, false),
    new(0x1601, 29, 21, false),
    new(0x5601, 15, 14, true),
    new(0x5401, 16, 14, false),
    new(0x5101, 17, 15, false),
    new(0x4801, 18, 16, false),
    new(0x3801, 19, 17, false),
    new(0x3401, 20, 18, false),
    new(0x3001, 21, 19, false),
    new(0x2801, 22, 19, false),
    new(0x2401, 23, 20, false),
    new(0x2201, 24, 21, false),
    new(0x1C01, 25, 22, false),
    new(0x1801, 26, 23, false),
    new(0x1601, 27, 24, false),
    new(0x1401, 28, 25, false),
    new(0x1201, 29, 26, false),
    new(0x1101, 30, 27, false),
    new(0x0AC1, 31, 28, false),
    new(0x09C1, 32, 29, false),
    new(0x08A1, 33, 30, false),
    new(0x0521, 34, 31, false),
    new(0x0441, 35, 32, false),
    new(0x02A1, 36, 33, false),
    new(0x0221, 37, 34, false),
    new(0x0141, 38, 35, false),
    new(0x0111, 39, 36, false),
    new(0x0085, 40, 37, false),
    new(0x0049, 41, 38, false),
    new(0x0025, 42, 39, false),
    new(0x0015, 43, 40, false),
    new(0x0009, 44, 41, false),
    new(0x0005, 45, 42, false),
    new(0x0001, 45, 43, false),
  ];

  private readonly MemoryStream _output = new();

  // Encoder registers
  private uint _c;
  private uint _a;
  private int _ct;
  private int _bp = -1; // byte pointer
  private int _sc;      // stack count for carry propagation
  private byte _b;      // current output byte

  internal Jbig2ArithmeticEncoder() {
    _a = 0x8000;
    _c = 0;
    _ct = 12;
    _sc = 0;
    _bp = -1;
  }

  /// <summary>Encodes a single decision using the given context (T.88 procedure ENCODE).</summary>
  internal void EncodeBit(Jbig2ContextModel.Context cx, int bit) {
    ref var entry = ref _QeTable[Math.Min(cx.I, _QeTable.Length - 1)];
    var qe = (uint)entry.Qe;

    _a -= qe;

    if (bit == cx.Mps) {
      // MPS path
      if ((_a & 0x8000) != 0)
        return; // no renorm needed

      if (_a < qe) {
        // Conditional exchange: code LPS
        _c += _a;
        _a = qe;
      }
      cx.I = entry.Nmps;
    } else {
      // LPS path
      if (_a >= qe) {
        _c += _a;
        _a = qe;
      }

      if (entry.SwitchFlag)
        cx.Mps = 1 - cx.Mps;

      cx.I = entry.Nlps;
    }

    _Renormalize();
  }

  /// <summary>Encodes an integer value using the JBIG2 integer coding procedure (T.88 6.4.6).</summary>
  internal void EncodeInteger(Jbig2ContextModel contexts, int value) {
    var prev = 1;
    var s = value < 0 ? 1 : 0;
    var v = Math.Abs(value);

    // Encode sign
    _EncodeBitWithCtx(contexts, ref prev, s);

    // Encode value prefix + bits
    if (v < 4) {
      _EncodeBitWithCtx(contexts, ref prev, 0);
      _EncodeIntBits(contexts, ref prev, v, 2);
    } else if (v < 20) {
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 0);
      _EncodeIntBits(contexts, ref prev, v - 4, 4);
    } else if (v < 84) {
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 0);
      _EncodeIntBits(contexts, ref prev, v - 20, 6);
    } else if (v < 340) {
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 0);
      _EncodeIntBits(contexts, ref prev, v - 84, 8);
    } else if (v < 4436) {
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 0);
      _EncodeIntBits(contexts, ref prev, v - 340, 12);
    } else {
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeBitWithCtx(contexts, ref prev, 1);
      _EncodeIntBits(contexts, ref prev, v - 4436, 32);
    }
  }

  /// <summary>Encodes an IAID symbol ID (T.88 6.4.10).</summary>
  internal void EncodeIaid(int symCodeLen, int[] iaidCtxI, int[] iaidCtxMps, int value) {
    var prev = 1;
    for (var i = symCodeLen - 1; i >= 0; --i) {
      var bit = (value >> i) & 1;
      _EncodeBitIaid(iaidCtxI, iaidCtxMps, prev, bit);
      prev = (prev << 1) | bit;
    }
  }

  private void _EncodeBitWithCtx(Jbig2ContextModel contexts, ref int prev, int bit) {
    EncodeBit(contexts[prev], bit);
    prev = ((prev << 1) | bit) & 0x1FF;
  }

  private void _EncodeIntBits(Jbig2ContextModel contexts, ref int prev, int value, int count) {
    for (var i = count - 1; i >= 0; --i) {
      var bit = (value >> i) & 1;
      EncodeBit(contexts[prev], bit);
      prev = ((prev << 1) | bit) & 0x1FF;
    }
  }

  private void _EncodeBitIaid(int[] iaidCtxI, int[] iaidCtxMps, int cx, int bit) {
    ref var entry = ref _QeTable[Math.Min(iaidCtxI[cx], _QeTable.Length - 1)];
    var qe = (uint)entry.Qe;

    _a -= qe;

    if (bit == iaidCtxMps[cx]) {
      if ((_a & 0x8000) != 0)
        return;

      if (_a < qe) {
        _c += _a;
        _a = qe;
      }
      iaidCtxI[cx] = entry.Nmps;
    } else {
      if (_a >= qe) {
        _c += _a;
        _a = qe;
      }
      if (entry.SwitchFlag)
        iaidCtxMps[cx] = 1 - iaidCtxMps[cx];
      iaidCtxI[cx] = entry.Nlps;
    }

    _Renormalize();
  }

  /// <summary>Encoder renormalization with carry propagation and byte-stuffing (T.88 E.2.4).</summary>
  private void _Renormalize() {
    while ((_a & 0x8000) == 0) {
      _a <<= 1;
      _c <<= 1;
      --_ct;
      if (_ct == 0)
        _ByteOut();
    }
  }

  /// <summary>Byte output with carry handling and 0xFF stuffing (T.88 E.2.5).</summary>
  private void _ByteOut() {
    var temp = (_c >> 19) & 0xFF;
    if (temp > 0xFF) {
      // Carry occurred
      if (_bp >= 0) {
        var buf = _output.GetBuffer();
        ++buf[_bp];
      }

      while (_sc > 0) {
        _output.WriteByte(0x00);
        --_sc;
      }

      _b = (byte)(temp & 0xFF);
      _bp = (int)_output.Position;
      _output.WriteByte(_b);
    } else if ((byte)temp == 0xFF) {
      ++_sc;
    } else {
      if (_bp >= 0) {
        // emit stacked bytes
        while (_sc > 0) {
          _output.WriteByte(0xFF);
          --_sc;
        }
      }

      _b = (byte)temp;
      _bp = (int)_output.Position;
      _output.WriteByte(_b);
    }

    _c &= 0x7FFFF;
    _ct = 8;
  }

  /// <summary>Finalizes the encoded stream (T.88 E.2.6 FLUSH).</summary>
  internal byte[] Finish() {
    // Set low bits of C
    var bits = 27 - 15 + _ct;
    _c <<= _ct;
    while (bits > 0) {
      _ByteOut();
      bits -= _ct;
      _c <<= _ct;
    }
    _ByteOut();

    return _output.ToArray();
  }
}
