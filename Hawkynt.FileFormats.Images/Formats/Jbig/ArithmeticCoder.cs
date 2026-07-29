using System;
using System.Collections.Generic;

namespace FileFormat.Jbig;

/// <summary>QM arithmetic encoder and decoder implementing the ITU-T T.82 probability estimation state machine.</summary>
internal static class ArithmeticCoder {

  /// <summary>A single entry in the Qe probability state table.</summary>
  internal readonly record struct QeEntry(int Qe, int Nmps, int Nlps, bool Switch);

  /// <summary>The 113-entry Qe probability state table from ITU-T T.82 Annex E.</summary>
  internal static readonly QeEntry[] QeTable = [
    new(0x5A1D,  1,  1, true ),  //  0
    new(0x2586,  2,  6, false),  //  1
    new(0x1114,  3,  9, false),  //  2
    new(0x080B,  4, 12, false),  //  3
    new(0x03D8,  5, 29, false),  //  4
    new(0x01DA,  6, 33, false),  //  5
    new(0x0015,  7,  6, true ),  //  6
    new(0x006F,  8, 14, false),  //  7
    new(0x0036,  9, 14, false),  //  8
    new(0x001A, 10, 14, false),  //  9
    new(0x000D, 11, 17, false),  // 10
    new(0x0006, 12, 18, false),  // 11
    new(0x0003, 13, 20, false),  // 12
    new(0x0001, 13, 21, false),  // 13
    new(0x5A7F, 15, 14, true ),  // 14
    new(0x3F25, 16, 14, false),  // 15
    new(0x2CF2, 17, 15, false),  // 16
    new(0x207C, 18, 16, false),  // 17
    new(0x17B9, 19, 17, false),  // 18
    new(0x1182, 20, 18, false),  // 19
    new(0x0CEF, 21, 19, false),  // 20
    new(0x09A1, 22, 19, false),  // 21
    new(0x072F, 23, 20, false),  // 22
    new(0x055C, 24, 21, false),  // 23
    new(0x0406, 25, 22, false),  // 24
    new(0x0303, 26, 23, false),  // 25
    new(0x0240, 27, 24, false),  // 26
    new(0x01B1, 28, 25, false),  // 27
    new(0x0144, 29, 26, false),  // 28
    new(0x00F5, 30, 27, false),  // 29
    new(0x00B7, 31, 28, false),  // 30
    new(0x008A, 32, 29, false),  // 31
    new(0x0068, 33, 30, false),  // 32
    new(0x004E, 34, 31, false),  // 33
    new(0x003B, 35, 32, false),  // 34
    new(0x002C, 36, 33, false),  // 35
    new(0x0021, 37, 34, false),  // 36
    new(0x0019, 38, 35, false),  // 37
    new(0x0012, 39, 36, false),  // 38
    new(0x000D, 40, 37, false),  // 39
    new(0x000A, 41, 38, false),  // 40
    new(0x0007, 42, 39, false),  // 41
    new(0x0005, 43, 40, false),  // 42
    new(0x0004, 44, 41, false),  // 43
    new(0x0003, 45, 42, false),  // 44
    new(0x0002, 46, 43, false),  // 45
    new(0x0001, 46, 46, false),  // 46
    new(0x5601, 48, 46, true ),  // 47
    new(0x5401, 49, 47, false),  // 48
    new(0x5101, 50, 48, false),  // 49
    new(0x4801, 51, 49, false),  // 50
    new(0x3C01, 52, 50, false),  // 51
    new(0x3401, 53, 51, false),  // 52
    new(0x3001, 54, 52, false),  // 53
    new(0x2401, 55, 53, false),  // 54
    new(0x1C01, 56, 54, false),  // 55
    new(0x1601, 57, 55, false),  // 56
    new(0x1201, 58, 56, false),  // 57
    new(0x0E01, 59, 57, false),  // 58
    new(0x0A01, 60, 58, false),  // 59
    new(0x0801, 61, 59, false),  // 60
    new(0x0601, 62, 60, false),  // 61
    new(0x0401, 63, 61, false),  // 62
    new(0x0301, 64, 62, false),  // 63
    new(0x0201, 65, 63, false),  // 64
    new(0x0101, 66, 64, false),  // 65
    new(0x0081, 67, 65, false),  // 66
    new(0x0041, 68, 66, false),  // 67
    new(0x0021, 69, 67, false),  // 68
    new(0x0011, 70, 68, false),  // 69
    new(0x0009, 71, 69, false),  // 70
    new(0x0005, 72, 70, false),  // 71
    new(0x0001, 73, 71, false),  // 72
    new(0x5601, 74, 72, true ),  // 73
    new(0x5401, 75, 73, false),  // 74
    new(0x5101, 76, 74, false),  // 75
    new(0x4801, 77, 75, false),  // 76
    new(0x3C01, 78, 76, false),  // 77
    new(0x3401, 79, 77, false),  // 78
    new(0x3001, 80, 78, false),  // 79
    new(0x2401, 81, 79, false),  // 80
    new(0x1C01, 82, 80, false),  // 81
    new(0x1601, 83, 81, false),  // 82
    new(0x1201, 84, 82, false),  // 83
    new(0x0E01, 85, 83, false),  // 84
    new(0x0A01, 86, 84, false),  // 85
    new(0x0801, 87, 85, false),  // 86
    new(0x0601, 88, 86, false),  // 87
    new(0x0401, 89, 87, false),  // 88
    new(0x0301, 90, 88, false),  // 89
    new(0x0201, 91, 89, false),  // 90
    new(0x0101, 92, 90, false),  // 91
    new(0x0081, 93, 91, false),  // 92
    new(0x0041, 94, 92, false),  // 93
    new(0x0021, 95, 93, false),  // 94
    new(0x0011, 96, 94, false),  // 95
    new(0x0009, 97, 95, false),  // 96
    new(0x0005, 98, 96, false),  // 97
    new(0x0001, 99, 97, false),  // 98
    new(0x5601,100, 98, true ),  // 99
    new(0x5401,101, 99, false),  //100
    new(0x5101,102,100, false),  //101
    new(0x4801,103,101, false),  //102
    new(0x3C01,104,102, false),  //103
    new(0x3401,105,103, false),  //104
    new(0x3001,106,104, false),  //105
    new(0x2401,107,105, false),  //106
    new(0x1C01,108,106, false),  //107
    new(0x1601,109,107, false),  //108
    new(0x1201,110,108, false),  //109
    new(0x0E01,111,109, false),  //110
    new(0x0A01,112,110, false),  //111
    new(0x0801,112,111, false),  //112
  ];

  /// <summary>
  /// Range-based arithmetic encoder with carry tracking using 64-bit low register.
  /// The interval is [low, low+range) with range normalized to [0x01000000, 0x100000000).
  /// </summary>
  internal sealed class Encoder {
    private ulong _low;
    private uint _range = 0x80000000u;
    private int _cache = -1;
    private long _pendingCount;
    private readonly List<byte> _output = new();

    public void EncodeBit(int bit, int cx, int[] states, int[] mps) {
      ref var st = ref states[cx];
      var qe = (uint)QeTable[st].Qe;

      // Scale Qe to 32-bit range
      var threshold = (_range >> 16) * qe;
      if (threshold < 1)
        threshold = 1;

      if (bit == mps[cx]) {
        _range -= threshold;
        st = QeTable[st].Nmps;
      } else {
        _low += _range - threshold;
        _range = threshold;
        if (QeTable[st].Switch)
          mps[cx] ^= 1;
        st = QeTable[st].Nlps;
      }

      _Normalize();
    }

    private void _Normalize() {
      while (_range < 0x01000000u) {
        // Check for carry in 64-bit low
        if (_low >= 0x100000000UL) {
          // Carry occurred
          _EmitByteWithCarry();
        } else if ((_low & 0xFF000000u) == 0xFF000000u) {
          // Byte is 0xFF, which might get a carry later -- defer
          ++_pendingCount;
          _low = (_low << 8) & 0xFFFFFFFF;
        } else {
          // Normal output
          _EmitByteNoCarry();
        }

        _range <<= 8;
      }
    }

    private void _EmitByteWithCarry() {
      if (_cache >= 0)
        _output.Add((byte)(_cache + 1));

      while (_pendingCount > 0) {
        _output.Add(0x00); // 0xFF + carry = 0x00
        --_pendingCount;
      }

      _cache = (int)((_low >> 24) & 0xFF);
      _low = (_low << 8) & 0xFFFFFFFF;
    }

    private void _EmitByteNoCarry() {
      if (_cache >= 0)
        _output.Add((byte)_cache);

      while (_pendingCount > 0) {
        _output.Add(0xFF);
        --_pendingCount;
      }

      _cache = (int)(_low >> 24);
      _low = (_low << 8) & 0xFFFFFFFF;
    }

    public byte[] Flush() {
      // Force the final interval
      _low += _range - 1;
      _range = 1;
      _Normalize();

      // Flush remaining cache and pending
      if (_cache >= 0) {
        if (_low >= 0x100000000UL) {
          _output.Add((byte)(_cache + 1));
          while (_pendingCount > 0) {
            _output.Add(0x00);
            --_pendingCount;
          }
        } else {
          _output.Add((byte)_cache);
          while (_pendingCount > 0) {
            _output.Add(0xFF);
            --_pendingCount;
          }
        }
      }

      // Output the remaining low bytes
      _output.Add((byte)((_low >> 24) & 0xFF));
      _output.Add((byte)((_low >> 16) & 0xFF));
      _output.Add((byte)((_low >> 8) & 0xFF));
      _output.Add((byte)(_low & 0xFF));

      return [.. _output];
    }
  }

  /// <summary>Range-based arithmetic decoder matching the encoder.</summary>
  internal sealed class Decoder {
    private readonly byte[] _data;
    private int _pos;
    private readonly int _end;
    private uint _range = 0x80000000u;
    private uint _code;

    public Decoder(byte[] data, int offset, int length) {
      _data = data;
      _pos = offset;
      _end = offset + length;

      // Read initial 4 bytes into code register
      _code = 0;
      for (var i = 0; i < 4; ++i)
        _code = (_code << 8) | _ReadByte();
    }

    private byte _ReadByte() => _pos < _end ? _data[_pos++] : (byte)0x00;

    public int DecodeBit(int cx, int[] states, int[] mps) {
      ref var st = ref states[cx];
      var qe = (uint)QeTable[st].Qe;

      var threshold = (_range >> 16) * qe;
      if (threshold < 1)
        threshold = 1;

      int d;
      if (_code < _range - threshold) {
        // MPS sub-interval [0, range - threshold)
        _range -= threshold;
        d = mps[cx];
        st = QeTable[st].Nmps;
      } else {
        // LPS sub-interval [range - threshold, range)
        _code -= _range - threshold;
        _range = threshold;
        d = 1 - mps[cx];
        if (QeTable[st].Switch)
          mps[cx] ^= 1;
        st = QeTable[st].Nlps;
      }

      _Normalize();
      return d;
    }

    private void _Normalize() {
      while (_range < 0x01000000u) {
        _range <<= 8;
        _code = (_code << 8) | _ReadByte();
      }
    }
  }
}
