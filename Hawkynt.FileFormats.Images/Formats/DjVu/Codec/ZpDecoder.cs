using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FileFormat.DjVu.Codec;

/// <summary>DjVu ZP adaptive binary arithmetic decoder.</summary>
/// <remarks>
/// The interval split, probability-state adaptation and renormalization follow the published ZP
/// coding process. Implementation provenance and compatible reference material are recorded in
/// <c>UPSTREAM.md</c> beside this file.
/// </remarks>
internal sealed class ZpDecoder {

  private readonly byte[] _source;
  private int _position;
  private uint _interval;
  private uint _code;
  private uint _fence;
  private uint _reservoir;
  private int _reservoirBits;
  private int _paddingReadsRemaining = 25;

  /// <summary>Whether the real input and the arithmetic decoder's permitted synthetic tail are exhausted.</summary>
  public bool IsEof => _position >= _source.Length && _paddingReadsRemaining <= 0;

  public ZpDecoder(byte[] data, int startOffset = 0) {
    ArgumentNullException.ThrowIfNull(data);

    _source = data;
    _position = startOffset;
    _code = (uint)(_ReadByte() << 8) | _ReadByte();
    _FillReservoir();
    _UpdateFence();
  }

  /// <summary>Decodes one context-adaptive bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeBit(ref ZpContext context) {
    var state = context.Value;
    var probableBit = state & 1;
    var split = _interval + ZpTables.P[state];

    if (split <= _fence) {
      _interval = split;
      return probableBit;
    }

    return _DecodeAdaptiveSlow(ref context, state, probableBit, split);
  }

  /// <summary>Decodes one equiprobable bit.</summary>
  public int DecodePassthrough()
    => _DecodePassthrough(0x8000u + (_interval >> 1));

  /// <summary>Decodes an unsigned integer, most-significant bit first, with one adaptive context.</summary>
  public int DecodeBinary(ref ZpContext context, int bits) {
    var result = 0;
    for (var bit = bits - 1; bit >= 0; --bit)
      result |= DecodeBit(ref context) << bit;
    return result;
  }

  private int _DecodeAdaptiveSlow(ref ZpContext context, byte state, int probableBit, uint split) {
    split = _RestrictSplit(split);

    if (split > _code) {
      var tail = 0x10000u - split;
      _interval = (_interval + tail) & 0xffff;
      _code = (_code + tail) & 0xffff;
      context = new(ZpTables.Dn[state]);
      _RenormalizeAfterLessProbable();
      _UpdateFence();
      return probableBit ^ 1;
    }

    if (_interval >= ZpTables.M[state])
      context = new(ZpTables.Up[state]);

    _ShiftOne(split);
    _UpdateFence();
    return probableBit;
  }

  private int _DecodePassthrough(uint split) {
    if (split > _code) {
      var tail = 0x10000u - split;
      _interval = (_interval + tail) & 0xffff;
      _code = (_code + tail) & 0xffff;
      _RenormalizeAfterLessProbable();
      _UpdateFence();
      return 1;
    }

    _ShiftOne(split);
    _UpdateFence();
    return 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private uint _RestrictSplit(uint split) {
    var limit = 0x6000u + ((_interval + split) >> 2);
    return Math.Min(split, limit);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _ShiftOne(uint split) {
    --_reservoirBits;
    _interval = (split << 1) & 0xffff;
    _code = ((_code << 1) | ((_reservoir >> _reservoirBits) & 1)) & 0xffff;
    if (_reservoirBits < 16)
      _FillReservoir();
  }

  private void _RenormalizeAfterLessProbable() {
    var shift = Math.Min(16, BitOperations.LeadingZeroCount((~_interval & 0xffffu) << 16));
    _reservoirBits -= shift;
    _interval = (_interval << shift) & 0xffff;
    var mask = (1u << shift) - 1;
    _code = ((_code << shift) | ((_reservoir >> _reservoirBits) & mask)) & 0xffff;
    if (_reservoirBits < 16)
      _FillReservoir();
  }

  private void _FillReservoir() {
    while (_reservoirBits <= 24) {
      _reservoir = (_reservoir << 8) | _ReadByte();
      _reservoirBits += 8;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private byte _ReadByte() {
    if (_position < _source.Length)
      return _source[_position++];

    --_paddingReadsRemaining;
    return 0xff;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _UpdateFence()
    => _fence = Math.Min(_code, 0x7fffu);
}
