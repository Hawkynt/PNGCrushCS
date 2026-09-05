using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>MQ arithmetic decoder for EBCOT tier-1 coding (ITU-T T.800 Annex C).</summary>
/// <remarks>
/// Pairs with <see cref="MqEncoder"/> and, more to the point, with every other implementation: the
/// LPS sub-interval is at the bottom of the interval, so the comparison that separates the two
/// branches is against Qe rather than against the remaining interval.
/// </remarks>
internal sealed class MqDecoder {

  private uint _a;
  private uint _c;
  private int _ct;

  private readonly byte[] _data;
  private readonly int _start;
  private readonly int _end;
  private int _bp;

  private readonly int[] _states;
  private readonly int[] _mps;

  public MqDecoder(byte[] data, int offset, int length, int numContexts) {
    ArgumentNullException.ThrowIfNull(data);
    if ((uint)offset > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(offset));
    if (length < 0 || offset + length > data.Length)
      throw new ArgumentOutOfRangeException(nameof(length));

    _data = data;
    _start = offset;
    _end = offset + length;
    _states = new int[numContexts];
    _mps = new int[numContexts];
    _Initialize();
  }

  /// <summary>Sets a context's initial state index and most probable symbol (Table D.7).</summary>
  internal void SetContext(int context, int stateIndex, int mpsValue) {
    _states[context] = stateIndex;
    _mps[context] = mpsValue;
  }

  /// <summary>Decodes one decision in the given context (C.3.2 DECODE).</summary>
  public int DecodeBit(int context) {
    var stateIndex = _states[context];
    var qe = (uint)MqTables.QE[stateIndex];
    _a -= qe;

    int decision;
    if ((_c >> 16) < qe) {
      decision = _LpsExchange(context, stateIndex, qe);
      _Renormalize();
      return decision;
    }

    _c -= qe << 16;
    if ((_a & 0x8000) != 0)
      return _mps[context];

    decision = _MpsExchange(context, stateIndex, qe);
    _Renormalize();
    return decision;
  }

  private int _MpsExchange(int context, int stateIndex, uint qe) {
    if (_a < qe) {
      var decision = 1 - _mps[context];
      if (MqTables.SWITCH[stateIndex] != 0)
        _mps[context] = decision;
      _states[context] = MqTables.NLPS[stateIndex];
      return decision;
    }

    _states[context] = MqTables.NMPS[stateIndex];
    return _mps[context];
  }

  private int _LpsExchange(int context, int stateIndex, uint qe) {
    if (_a < qe) {
      _a = qe;
      var mps = _mps[context];
      _states[context] = MqTables.NMPS[stateIndex];
      return mps;
    }

    _a = qe;
    var decision = 1 - _mps[context];
    if (MqTables.SWITCH[stateIndex] != 0)
      _mps[context] = decision;
    _states[context] = MqTables.NLPS[stateIndex];
    return decision;
  }

  private void _Initialize() {
    _bp = _start;
    _c = _bp < _end ? (uint)_data[_bp] << 16 : 0xFF0000u;
    _ByteIn();
    _c <<= 7;
    _ct -= 7;
    _a = 0x8000;
  }

  /// <summary>
  /// C.3.4 BYTEIN. Past the end of the segment the decoder feeds itself 1-bits, which is what lets a
  /// truncated code-block still decode the passes that are present.
  /// </summary>
  private void _ByteIn() {
    if (_bp >= _end) {
      _c += 0xFF00;
      _ct = 8;
      return;
    }

    var next = _bp + 1 < _end ? _data[_bp + 1] : (byte)0xFF;
    if (_data[_bp] == 0xFF) {
      if (next > 0x8F) {
        // A marker follows; it is not part of the code-block and must not be consumed.
        _c += 0xFF00;
        _ct = 8;
        return;
      }

      ++_bp;
      _c += (uint)next << 9;
      _ct = 7;
      return;
    }

    ++_bp;
    _c += (uint)next << 8;
    _ct = 8;
  }

  private void _Renormalize() {
    do {
      if (_ct == 0)
        _ByteIn();

      _a <<= 1;
      _c <<= 1;
      --_ct;
    } while ((_a & 0x8000) == 0);
  }
}
