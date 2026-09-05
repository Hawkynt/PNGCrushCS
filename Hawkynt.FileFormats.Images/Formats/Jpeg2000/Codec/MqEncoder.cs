using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>MQ arithmetic encoder for EBCOT tier-1 coding (ITU-T T.800 Annex C).</summary>
/// <remarks>
/// The interval convention is the one the standard's flow charts use: the LPS sub-interval sits at
/// the bottom of the current interval, so coding an MPS adds Qe to the code register and coding an
/// LPS leaves it alone. An earlier version of this class had the two halves the other way round.
/// That is self-consistent — its own decoder read its own output — but it is a different code, and
/// no other implementation could follow it.
/// </remarks>
internal sealed class MqEncoder {

  /// <summary>Interval register.</summary>
  private uint _a;

  /// <summary>Code register.</summary>
  private uint _c;

  /// <summary>Bits remaining before the next byte leaves the code register.</summary>
  private int _ct;

  /// <summary>
  /// Output bytes. Index zero is a scratch byte that is never emitted; carry propagation increments
  /// the byte already written, and starting one byte early means the very first BYTEOUT has
  /// somewhere to carry into.
  /// </summary>
  private readonly List<byte> _bytes = [0];

  /// <summary>Index of the most recently written byte.</summary>
  private int _bp;

  private readonly int[] _states;
  private readonly int[] _mps;

  public MqEncoder(int numContexts) {
    _states = new int[numContexts];
    _mps = new int[numContexts];
    _a = 0x8000;
    _c = 0;
    _ct = 12;
    _bp = 0;
  }

  /// <summary>Sets a context's initial state index and most probable symbol (Table D.7).</summary>
  internal void SetContext(int context, int stateIndex, int mpsValue) {
    _states[context] = stateIndex;
    _mps[context] = mpsValue;
  }

  /// <summary>Encodes one decision in the given context.</summary>
  public void EncodeBit(int context, int bit) {
    if (bit == _mps[context])
      _CodeMps(context);
    else
      _CodeLps(context);
  }

  /// <summary>Terminates the codeword segment and returns its bytes (C.2.9 FLUSH).</summary>
  public byte[] Flush() {
    _SetBits();
    _c <<= _ct;
    _ByteOut();
    _c <<= _ct;
    _ByteOut();

    // A terminating 0xFF carries no information and the standard allows dropping it; OpenJPEG does,
    // and a code-block contribution is not permitted to end on 0xFF in a packet body anyway.
    var count = _bytes[_bp] == 0xFF ? _bp - 1 : _bp;
    var result = new byte[count];
    _bytes.CopyTo(1, result, 0, count);
    return result;
  }

  private void _CodeMps(int context) {
    var stateIndex = _states[context];
    var qe = (uint)MqTables.QE[stateIndex];
    _a -= qe;

    if ((_a & 0x8000) != 0) {
      _c += qe;
      return;
    }

    if (_a < qe)
      _a = qe;
    else
      _c += qe;

    _states[context] = MqTables.NMPS[stateIndex];
    _Renormalize();
  }

  private void _CodeLps(int context) {
    var stateIndex = _states[context];
    var qe = (uint)MqTables.QE[stateIndex];
    _a -= qe;

    if (_a < qe)
      _c += qe;
    else
      _a = qe;

    if (MqTables.SWITCH[stateIndex] != 0)
      _mps[context] = 1 - _mps[context];

    _states[context] = MqTables.NLPS[stateIndex];
    _Renormalize();
  }

  private void _Renormalize() {
    do {
      _a <<= 1;
      _c <<= 1;
      --_ct;
      if (_ct == 0)
        _ByteOut();
    } while ((_a & 0x8000) == 0);
  }

  /// <summary>C.2.8 BYTEOUT, including the bit stuffing that follows an emitted 0xFF.</summary>
  private void _ByteOut() {
    if (_bytes[_bp] == 0xFF) {
      _Append((byte)(_c >> 20));
      _c &= 0xFFFFF;
      _ct = 7;
      return;
    }

    if ((_c & 0x8000000) == 0) {
      _Append((byte)(_c >> 19));
      _c &= 0x7FFFF;
      _ct = 8;
      return;
    }

    _bytes[_bp] = (byte)(_bytes[_bp] + 1);
    if (_bytes[_bp] == 0xFF) {
      _c &= 0x7FFFFFF;
      _Append((byte)(_c >> 20));
      _c &= 0xFFFFF;
      _ct = 7;
      return;
    }

    _Append((byte)(_c >> 19));
    _c &= 0x7FFFF;
    _ct = 8;
  }

  private void _Append(byte value) {
    ++_bp;
    if (_bp == _bytes.Count)
      _bytes.Add(value);
    else
      _bytes[_bp] = value;
  }

  private void _SetBits() {
    var temp = _c + _a;
    _c |= 0xFFFF;
    if (_c >= temp)
      _c -= 0x8000;
  }
}
