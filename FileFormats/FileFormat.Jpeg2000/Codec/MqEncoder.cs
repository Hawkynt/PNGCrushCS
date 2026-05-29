using System;
using System.IO;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>MQ (binary arithmetic) encoder used in JPEG 2000 EBCOT tier-1 coding (ITU-T T.800 Section C).</summary>
internal sealed class MqEncoder {

  /// <summary>Interval register (16-bit range).</summary>
  private uint _a;

  /// <summary>Code register.</summary>
  private uint _c;

  /// <summary>Counter (bits before next output).</summary>
  private int _ct;

  /// <summary>Delayed output byte (-1 = none pending).</summary>
  private int _buffer;

  /// <summary>Count of stacked 0xFF bytes pending output.</summary>
  private int _delayedFF;

  private readonly MemoryStream _output;

  /// <summary>Current state index per context.</summary>
  private readonly int[] _states;

  /// <summary>Most probable symbol per context.</summary>
  private readonly int[] _mps;

  public MqEncoder(int numContexts) {
    _output = new MemoryStream();
    _states = new int[numContexts];
    _mps = new int[numContexts];
    _Initialize();
  }

  /// <summary>Encode one bit in the given context (ITU-T T.800 Section C.2).</summary>
  public void EncodeBit(int context, int bit) {
    var stateIdx = _states[context];
    var qe = (uint)MqTables.QE[stateIdx];
    _a -= qe;

    if (bit == _mps[context]) {
      // Encode MPS
      if (_a >= 0x8000)
        return; // C unchanged, no renormalization

      if (_a < qe) {
        // Conditional exchange: code LPS interval instead
        _c += _a;
        _a = qe;
      }
      _states[context] = MqTables.NMPS[stateIdx];
    } else {
      // Encode LPS
      if (_a >= qe) {
        _c += _a;
        _a = qe;
      }

      if (MqTables.SWITCH[stateIdx] != 0)
        _mps[context] = 1 - _mps[context];

      _states[context] = MqTables.NLPS[stateIdx];
    }

    _Renormalize();
  }

  /// <summary>Flush remaining bits and return the encoded byte array.</summary>
  public byte[] Flush() {
    _SetBits();
    _c <<= _ct;
    _ByteOut();
    _c <<= _ct;
    _ByteOut();

    if (_buffer >= 0)
      _output.WriteByte((byte)_buffer);

    // Emit any pending 0xFF bytes
    while (_delayedFF > 0) {
      _output.WriteByte(0xFF);
      --_delayedFF;
    }

    return _output.ToArray();
  }

  /// <summary>Set a context's initial state and MPS value.</summary>
  internal void SetContext(int context, int stateIndex, int mpsValue) {
    _states[context] = stateIndex;
    _mps[context] = mpsValue;
  }

  private void _Initialize() {
    _a = 0x8000;
    _c = 0;
    _ct = 12;
    _buffer = -1;
    _delayedFF = 0;
  }

  private void _Renormalize() {
    while (_a < 0x8000) {
      _a <<= 1;
      _c <<= 1;
      --_ct;
      if (_ct == 0)
        _ByteOut();
    }
  }

  private void _ByteOut() {
    var temp = _c >> 19;
    if (temp > 0xFF) {
      // Carry propagation
      if (_buffer >= 0)
        _output.WriteByte((byte)(_buffer + 1));

      while (_delayedFF > 0) {
        _output.WriteByte(0x00); // 0xFF + carry = 0x100 -> emit 0x00
        --_delayedFF;
      }

      _buffer = (int)(temp & 0xFF);
    } else if (temp == 0xFF) {
      ++_delayedFF;
    } else {
      if (_buffer >= 0)
        _output.WriteByte((byte)_buffer);

      while (_delayedFF > 0) {
        _output.WriteByte(0xFF);
        --_delayedFF;
      }

      _buffer = (int)(temp & 0xFF);
    }

    _c &= 0x7FFFF;
    _ct = 8;
  }

  private void _SetBits() {
    var temp = _a - 1 + _c;
    temp &= 0xFFFF0000;
    if (temp < _c)
      temp += 0x8000;

    _c = temp;
  }
}
