using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Range coder decoder. LZMA-style with single-step normalization per operation.
/// Both encoder and decoder use the exact same interval arithmetic.
/// </summary>
internal sealed class FlifRangeDecoder {

  private const uint _TOP = 1u << 24;

  private readonly byte[] _data;
  private int _pos;
  private uint _range;
  private uint _code;

  public bool IsEof => _pos >= _data.Length;

  public FlifRangeDecoder(byte[] data, int startOffset) {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
    _pos = startOffset;
    _code = 0;
    _range = uint.MaxValue;
    for (var i = 0; i < 5; ++i)
      _code = (_code << 8) | _In();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeBit(int chanceOf1) {
    var bound = (_range >> 12) * (uint)(4096 - chanceOf1);

    if (_code < bound) {
      _range = bound;
      _Norm();
      return 0;
    }

    _range -= bound;
    _code -= bound;
    _Norm();
    return 1;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeEquiprobable() {
    _range >>= 1;

    if (_code < _range) {
      _Norm();
      return 0;
    }

    _code -= _range;
    _Norm();
    return 1;
  }

  public int DecodeUniform(int max) {
    if (max <= 0)
      return 0;
    var bits = _BitsFor((uint)max);
    var v = 0;
    for (var i = bits - 1; i >= 0; --i)
      v |= DecodeEquiprobable() << i;
    return Math.Min(v, max);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Norm() {
    if (_range < _TOP) {
      _range <<= 8;
      _code = (_code << 8) | _In();
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private uint _In() => _pos < _data.Length ? _data[_pos++] : 0u;

  private static int _BitsFor(uint v) {
    var b = 0;
    while ((1u << b) <= v) ++b;
    return b;
  }
}

/// <summary>
/// Range coder encoder. Exactly mirrors <see cref="FlifRangeDecoder"/>.
/// Uses 64-bit low for natural carry handling.
/// </summary>
internal sealed class FlifRangeEncoder {

  private const uint _TOP = 1u << 24;

  private readonly List<byte> _output;
  private ulong _low;
  private uint _range;
  private byte _cache;
  private uint _cacheSize;

  public FlifRangeEncoder(int capacity = 4096) {
    _output = new(capacity);
    _low = 0;
    _range = uint.MaxValue;
    _cache = 0;
    _cacheSize = 1;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void EncodeBit(int bit, int chanceOf1) {
    var bound = (_range >> 12) * (uint)(4096 - chanceOf1);

    if (bit == 0)
      _range = bound;
    else {
      _low += bound;
      _range -= bound;
    }

    _Norm();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void EncodeEquiprobable(int bit) {
    _range >>= 1;
    if (bit != 0)
      _low += _range;
    _Norm();
  }

  public void EncodeUniform(int value, int max) {
    if (max <= 0)
      return;
    var bits = _BitsFor((uint)max);
    for (var i = bits - 1; i >= 0; --i)
      EncodeEquiprobable((value >> i) & 1);
  }

  public byte[] Finish() {
    for (var i = 0; i < 5; ++i)
      _ShiftLow();
    return [.. _output];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Norm() {
    if (_range < _TOP) {
      _range <<= 8;
      _ShiftLow();
    }
  }

  private void _ShiftLow() {
    // Check if a carry occurred (bit 32+ set in _low)
    if ((uint)_low < 0xFF000000u || (_low >> 32) != 0) {
      var carry = (byte)(_low >> 32);
      _output.Add((byte)(_cache + carry));
      var fill = carry != 0 ? (byte)0x00 : (byte)0xFF;
      for (uint i = 1; i < _cacheSize; ++i)
        _output.Add(fill);
      _cacheSize = 0;
      _cache = (byte)((uint)_low >> 24);
    }

    ++_cacheSize;
    _low = (uint)((uint)_low << 8);
  }

  private static int _BitsFor(uint v) {
    var b = 0;
    while ((1u << b) <= v) ++b;
    return b;
  }
}
