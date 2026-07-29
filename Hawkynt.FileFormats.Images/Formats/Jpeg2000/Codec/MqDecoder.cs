using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>MQ (binary arithmetic) decoder used in JPEG 2000 EBCOT tier-1 coding (ITU-T T.800 Section D).</summary>
internal sealed class MqDecoder {

  /// <summary>Interval register (16-bit range).</summary>
  private uint _a;

  /// <summary>Code register (upper 16 bits of C hold the active value).</summary>
  private uint _c;

  /// <summary>Counter (bits remaining before next byte-in).</summary>
  private int _ct;

  private readonly byte[] _data;
  private int _pos;
  private readonly int _end;
  private byte _lastByte;

  /// <summary>Current state index per context.</summary>
  private readonly int[] _states;

  /// <summary>Most probable symbol (0 or 1) per context.</summary>
  private readonly int[] _mps;

  public MqDecoder(byte[] data, int offset, int length, int numContexts) {
    _data = data;
    _pos = offset;
    _end = offset + length;
    _states = new int[numContexts];
    _mps = new int[numContexts];
    _Initialize();
  }

  /// <summary>Decode one bit in the given context (ITU-T T.800 Section D.2).</summary>
  public int DecodeBit(int context) {
    var stateIdx = _states[context];
    var qe = (uint)MqTables.QE[stateIdx];
    _a -= qe;

    int d;
    if ((_c >> 16) < _a) {
      // MPS sub-interval: C_active < A
      if (_a >= 0x8000)
        return _mps[context];

      if (_a < qe) {
        // Conditional exchange: LPS is actually more probable in this interval
        d = 1 - _mps[context];
        if (MqTables.SWITCH[stateIdx] != 0)
          _mps[context] = 1 - _mps[context];
        _states[context] = MqTables.NLPS[stateIdx];
        _a = qe;
      } else {
        d = _mps[context];
        _states[context] = MqTables.NMPS[stateIdx];
      }
    } else {
      // LPS sub-interval: C_active >= A
      _c -= _a << 16;
      if (_a < qe) {
        // Conditional exchange: MPS
        d = _mps[context];
        _states[context] = MqTables.NMPS[stateIdx];
      } else {
        d = 1 - _mps[context];
        if (MqTables.SWITCH[stateIdx] != 0)
          _mps[context] = 1 - _mps[context];
        _states[context] = MqTables.NLPS[stateIdx];
        _a = qe;
      }
    }

    _Renormalize();
    return d;
  }

  /// <summary>Set a context's initial state and MPS value.</summary>
  internal void SetContext(int context, int stateIndex, int mpsValue) {
    _states[context] = stateIndex;
    _mps[context] = mpsValue;
  }

  private void _Initialize() {
    // INITDEC procedure (ITU-T T.800 Section D.2.6)
    _lastByte = 0;
    _a = 0x8000;
    _c = 0;

    // Read first byte
    if (_pos < _end) {
      _lastByte = _data[_pos];
      ++_pos;
    }

    _c = (uint)_lastByte << 16;
    _ByteIn();
    _c <<= 7;
    _ct -= 7;
    _a = 0x8000;
  }

  private void _ByteIn() {
    if (_lastByte == 0xFF) {
      var nextByte = _pos < _end ? _data[_pos] : (byte)0xFF;
      if (nextByte > 0x8F) {
        // Marker segment detected: do not consume the marker
        _ct = 8;
      } else {
        ++_pos;
        _lastByte = nextByte;
        _c += (uint)_lastByte << 9;
        _ct = 7;
      }
    } else {
      if (_pos < _end) {
        _lastByte = _data[_pos];
        ++_pos;
      } else
        _lastByte = 0xFF;

      _c += (uint)_lastByte << 8;
      _ct = 8;
    }
  }

  private void _Renormalize() {
    while (_a < 0x8000) {
      if (_ct == 0)
        _ByteIn();

      _a <<= 1;
      _c <<= 1;
      --_ct;
    }
  }
}
